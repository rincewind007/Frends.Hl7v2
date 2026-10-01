using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using ConvertToXmlTask = Frends.Hl7v2.ConvertToXml.Hl7v2;
using ConvertToXmlDefs = Frends.Hl7v2.ConvertToXml.Definitions;
using CreateFromXmlTask = TjPTestCreateFromXml.Hl7v2;
using CreateFromXmlDefs = TjPTestCreateFromXml.Definitions;

namespace Frends.Hl7v2.RoundTripRunner;

/// <summary>
/// Standalone "bin": runs the ConvertToXml -> CreateFromXml round trip over
/// every .hl7/.txt file in the shared HL7Messages/ folder (the same folder
/// Frends.Hl7v2.RoundTripTests case 14 reads from), and prints a plain
/// pass/fail summary - how many of each, and which specific file(s) failed -
/// without needing dotnet test/NUnit. Meant for quickly sanity-checking a
/// large dump of real-world HL7 files (100+) in one run.
///
/// Usage:
///   dotnet run --project Frends.Hl7v2.RoundTripRunner [path-to-HL7Messages-folder]
/// With no argument, defaults to the sibling
/// Frends.Hl7v2.RoundTripTests/HL7Messages folder.
///
/// PASS means ConvertToXml and CreateFromXml both completed without
/// throwing/returning Success=false - NOT that the round trip was
/// byte-identical (a successful-but-lossy round trip is a legitimate, already
/// understood outcome for some real-world messages - see
/// Frends.Hl7v2.RoundTripTests cases 2, 11, 12). Byte-identical-or-not is
/// still reported per file as informational.
///
/// Exit code: 0 if every file passed, 1 if any file failed.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var folder = args.Length > 0 ? Path.GetFullPath(args[0]) : DefaultHl7MessagesFolder();

        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"HL7Messages folder not found: {folder}");
            Console.Error.WriteLine("Pass a folder path as the first argument to override, e.g.:");
            Console.Error.WriteLine("  dotnet run --project Frends.Hl7v2.RoundTripRunner -- \"C:\\path\\to\\folder\"");
            return 2;
        }

        var files = Directory.EnumerateFiles(folder, "*.hl7")
            .Concat(Directory.EnumerateFiles(folder, "*.txt"))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine($"HL7 round-trip runner");
        Console.WriteLine($"Folder: {folder}");
        Console.WriteLine($"Found {files.Count} file(s).");
        Console.WriteLine();

        var runFolder = Path.Combine(RunnerProjectRoot(), "RunOutput", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(runFolder);

        var results = new List<FileResult>();

        foreach (var path in files)
        {
            var fileName = Path.GetFileName(path);
            var result = RunOne(path);
            results.Add(result);

            var status = result.Passed ? (result.Identical ? "PASS (identical)" : "PASS (lossy)") : "FAIL";
            Console.WriteLine($"[{status,-16}] {fileName}");

            if (!result.Passed)
            {
                var failFolder = Path.Combine(runFolder, Path.GetFileNameWithoutExtension(fileName));
                Directory.CreateDirectory(failFolder);
                File.WriteAllText(Path.Combine(failFolder, "error.txt"), result.ErrorMessage ?? "(no error message)");
                if (result.IntermediateXml != null)
                    File.WriteAllText(Path.Combine(failFolder, "intermediate.xml"), result.IntermediateXml);
            }
        }

        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);
        var identical = results.Count(r => r.Passed && r.Identical);
        var lossy = results.Count(r => r.Passed && !r.Identical);

        var summary = new StringBuilder();
        summary.AppendLine();
        summary.AppendLine("Summary");
        summary.AppendLine("-------");
        summary.AppendLine($"Total:            {results.Count}");
        summary.AppendLine($"Passed:           {passed} (byte-identical: {identical}, lossy-but-successful: {lossy})");
        summary.AppendLine($"Failed:           {failed}");

        if (failed > 0)
        {
            summary.AppendLine();
            summary.AppendLine("Failed file(s):");
            foreach (var r in results.Where(r => !r.Passed))
                summary.AppendLine($"  - {r.FileName}: {r.ErrorMessage}");
        }

        Console.Write(summary.ToString());

        File.WriteAllText(Path.Combine(runFolder, "summary.txt"), summary.ToString());
        Console.WriteLine();
        Console.WriteLine($"Run details saved to: {runFolder}");

        return failed == 0 ? 0 : 1;
    }

    private static FileResult RunOne(string path)
    {
        var fileName = Path.GetFileName(path);

        try
        {
            var original = Encoding.Latin1.GetString(File.ReadAllBytes(path));
            var normalizedOriginal = original.Replace("\r\n", "\r").Replace("\n", "\r").TrimEnd('\r');

            var toXmlResult = ConvertToXmlTask.ConvertToXml(
                new ConvertToXmlDefs.Input { Hl7v2Message = original },
                new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
                CancellationToken.None);

            if (!toXmlResult.Success)
                return FileResult.Fail(fileName, $"ConvertToXml failed: {toXmlResult.Error?.Message}");

            var toHl7Result = CreateFromXmlTask.CreateFromXml(
                new CreateFromXmlDefs.Input { Xml = toXmlResult.Xml },
                new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR },
                CancellationToken.None);

            if (!toHl7Result.Success)
                return FileResult.Fail(fileName, $"CreateFromXml failed: {toHl7Result.Error?.Message}", toXmlResult.Xml);

            var roundTripped = toHl7Result.Hl7v2Message.TrimEnd('\r');
            var identical = roundTripped == normalizedOriginal;

            return FileResult.Pass(fileName, identical);
        }
        catch (Exception ex)
        {
            return FileResult.Fail(fileName, $"Threw {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string DefaultHl7MessagesFolder() =>
        Path.GetFullPath(Path.Combine(RunnerProjectRoot(), "..", "Frends.Hl7v2.RoundTripTests", "HL7Messages"));

    private static string RunnerProjectRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private sealed class FileResult
    {
        public string FileName { get; private init; }

        public bool Passed { get; private init; }

        public bool Identical { get; private init; }

        public string ErrorMessage { get; private init; }

        public string IntermediateXml { get; private init; }

        public static FileResult Pass(string fileName, bool identical) =>
            new() { FileName = fileName, Passed = true, Identical = identical };

        public static FileResult Fail(string fileName, string errorMessage, string intermediateXml = null) =>
            new() { FileName = fileName, Passed = false, ErrorMessage = errorMessage, IntermediateXml = intermediateXml };
    }
}
