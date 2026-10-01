using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using NHapi.Base.Parser;
using NUnit.Framework;
using ConvertToXmlTask = Frends.Hl7v2.ConvertToXml.Hl7v2;
using ConvertToXmlDefs = Frends.Hl7v2.ConvertToXml.Definitions;
using CreateFromXmlTask = TjPTestCreateFromXml.Hl7v2;
using CreateFromXmlDefs = TjPTestCreateFromXml.Definitions;

namespace Frends.Hl7v2.RoundTripTests;

/// <summary>
/// Scratch/local tests: take a hand-written HL7v2 message, run it through
/// ConvertToXml then CreateFromXml, and diff the round-tripped message against
/// the original. Every test case writes its input/output/diff artifacts into its
/// own numbered subfolder under a single timestamped run folder (see
/// <see cref="RunFolder"/>). Each <c>dotnet test</c> invocation gets its own new
/// timestamped folder; nothing from previous runs is deleted, so
/// TestRunOutput accumulates one folder per run as a history.
/// This project is standalone and is not referenced by, or added to, any of the
/// existing task solutions.
/// </summary>
public class RoundTripTests
{
    // One new timestamped folder per test *run* (computed once when this fixture
    // is loaded); every test case gets its own "<n>_<CaseName>" subfolder under
    // it. Previous runs' folders are left alone - TestRunOutput is a history, not
    // a single overwritten snapshot.
    private static readonly string RunFolder = Path.Combine(
        GetProjectRoot(), "TestRunOutput", DateTime.Now.ToString("yyyyMMdd_HHmmss"));

    private static string GetProjectRoot() =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

    private static string CreateCaseFolder(int caseNumber, string caseName)
    {
        var folder = Path.Combine(RunFolder, $"{caseNumber}_{caseName}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void Save(string folder, string fileName, string content)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, fileName), content ?? string.Empty);
    }

    private static string Normalize(string hl7) =>
        hl7.Replace("\r\n", "\r").Replace("\n", "\r").TrimEnd('\r');

    private static string ReadSample(string fileName) =>
        File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", fileName));

    // HL7Messages/ is the bulk real-world dump folder: raw exports from a real
    // integration (Cerner Millennium / Syngo Dynamics via Region Skane),
    // declared ISO-8859-1 (MSH-18 "8859/1" / "ISO_IR 100") - read them by that
    // encoding, not UTF-8, or the Swedish characters (a, a, o with diacritics)
    // get corrupted on read. Meant to be dropped into freely: any .hl7/.txt file
    // added here is automatically picked up both by AllHl7MessagesInFolder
    // (case 14, one dynamically-generated test per file) and by the standalone
    // Frends.Hl7v2.RoundTripRunner console app.
    internal static readonly string Hl7MessagesFolder = Path.Combine(TestContext.CurrentContext.TestDirectory, "HL7Messages");

    private static string ReadRealWorldSample(string fileName) =>
        Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(Hl7MessagesFolder, fileName)));

    // ---------------------------------------------------------------------
    // Case 1: positive control - a well-formed Z-segment round-trips identically.
    // ---------------------------------------------------------------------
    [TestCase("SampleOruWithZSegment.hl7")]
    public void Hl7_ToXml_ToHl7_RoundTrip_IsIdentical(string fileName)
    {
        var caseFolder = CreateCaseFolder(1, "RoundTrip_IsIdentical");
        var (original, normalizedOriginal, roundTripped, xml) = RunRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff.Length == 0 ? "(none - identical)" : diff);

        if (roundTripped == normalizedOriginal)
            return;

        Assert.Fail(
            $"Round-tripped HL7 message differs from the original, but was expected to be identical.{Environment.NewLine}" +
            $"{Environment.NewLine}--- diff ---{Environment.NewLine}{diff}");
    }

    // ---------------------------------------------------------------------
    // Case 2: negative control - trailing empty non-standard fields are lost.
    // ---------------------------------------------------------------------
    [TestCase("SampleWithLossyNonStandardFields.hl7")]
    public void Hl7_ToXml_ToHl7_RoundTrip_DetectsLossyNonStandardFields(string fileName)
    {
        var caseFolder = CreateCaseFolder(2, "RoundTrip_DetectsLossyNonStandardFields");
        var (original, normalizedOriginal, roundTripped, xml) = RunRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff);

        Assert.That(
            roundTripped,
            Is.Not.EqualTo(normalizedOriginal),
            "Expected the trailing empty fields on the ZLB segment to be lost on round trip, " +
            "but the message came back identical - either the parser behavior changed, or this " +
            "test case no longer exercises a lossy path.");

        var originalZlb = normalizedOriginal.Split('\r').Single(l => l.StartsWith("ZLB|", StringComparison.Ordinal));
        var roundTrippedZlb = roundTripped.Split('\r').Single(l => l.StartsWith("ZLB|", StringComparison.Ordinal));

        Assert.That(originalZlb, Is.EqualTo("ZLB|1|CUSTOM-LABEL-ALPHA|NON-STD-UNIT|||||"));
        Assert.That(
            roundTrippedZlb,
            Is.EqualTo("ZLB|1|CUSTOM-LABEL-ALPHA|NON-STD-UNIT"),
            "The specific trailing-empty-field truncation this test targets did not occur as expected; " +
            "the ZLB line differs in some other way instead.");
    }

    // ---------------------------------------------------------------------
    // Case 3: negative control - a manual edit of the intermediate XML (patient's
    // given name John -> Jonny) is carried through and shows up in the diff.
    // ---------------------------------------------------------------------
    [TestCase("SampleOruWithZSegment.hl7")]
    public void Hl7_ToXml_ToHl7_RoundTrip_DetectsPatientNameMutatedInIntermediateXml(string fileName)
    {
        var caseFolder = CreateCaseFolder(3, "RoundTrip_DetectsPatientNameMutatedInIntermediateXml");

        var original = ReadSample(fileName);
        var normalizedOriginal = Normalize(original);

        var toXmlResult = ConvertToXmlTask.ConvertToXml(
            new ConvertToXmlDefs.Input { Hl7v2Message = original },
            new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
            CancellationToken.None);
        Assert.That(toXmlResult.Success, Is.True, () => $"ConvertToXml failed: {toXmlResult.Error?.Message}");

        // Simulate someone hand-editing the intermediate XML: change the patient's
        // given name (PID-5.2, XPN.2) from John to Jonny.
        const string originalGivenNameXml = "<XPN.2>JOHN</XPN.2>";
        const string mutatedGivenNameXml = "<XPN.2>JONNY</XPN.2>";
        Assert.That(
            toXmlResult.Xml,
            Does.Contain(originalGivenNameXml),
            "Test assumes the intermediate XML contains the patient's given name as <XPN.2>JOHN</XPN.2> - " +
            "update this test if ConvertToXml's XML shape or TestData/SampleOruWithZSegment.hl7 changes.");
        var mutatedXml = toXmlResult.Xml.Replace(originalGivenNameXml, mutatedGivenNameXml);

        var toHl7Result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = mutatedXml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR },
            CancellationToken.None);
        Assert.That(toHl7Result.Success, Is.True, () => $"CreateFromXml failed: {toHl7Result.Error?.Message}");
        var roundTripped = toHl7Result.Hl7v2Message.TrimEnd('\r');
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", toXmlResult.Xml);
        Save(caseFolder, "intermediate.mutated.xml", mutatedXml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff);

        Assert.That(
            roundTripped,
            Is.Not.EqualTo(normalizedOriginal),
            "Expected the mutated given name to produce a visible difference from the original.");

        var originalPid = normalizedOriginal.Split('\r').Single(l => l.StartsWith("PID|", StringComparison.Ordinal));
        var roundTrippedPid = roundTripped.Split('\r').Single(l => l.StartsWith("PID|", StringComparison.Ordinal));

        Assert.That(originalPid, Does.Contain("SMITH^JOHN"));
        Assert.That(roundTrippedPid, Does.Contain("SMITH^JONNY"));
        Assert.That(roundTrippedPid, Does.Not.Contain("SMITH^JOHN"));
        Assert.That(diff, Does.Contain("SMITH^JOHN"));
        Assert.That(diff, Does.Contain("SMITH^JONNY"));
    }

    // ---------------------------------------------------------------------
    // Cases 4-7: CreateFromXml.Options coverage - for every boolean option, prove
    // that true and false actually produce a different, expected outcome. Each
    // scenario is engineered so the option under test is the only thing that
    // changes the result.
    // ---------------------------------------------------------------------

    /// <summary>
    /// CorrectWhitespaces: true trims/collapses whitespace nhapi considers
    /// insignificant, false preserves it exactly.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void CreateFromXmlOption_CorrectWhitespaces(bool correctWhitespaces)
    {
        var caseFolder = CreateCaseFolder(4, "Option_CorrectWhitespaces");
        var subFolder = Path.Combine(caseFolder, correctWhitespaces ? "true" : "false");

        var baseXml = GetBaseXml();
        const string originalValueXml = "<OBX.5>42.7</OBX.5>";
        const string paddedValueXml = "<OBX.5>   42.7   </OBX.5>";
        Assert.That(baseXml, Does.Contain(originalValueXml));
        var paddedXml = baseXml.Replace(originalValueXml, paddedValueXml);

        var result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = paddedXml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR, CorrectWhitespaces = correctWhitespaces },
            CancellationToken.None);

        Save(subFolder, "input.xml", paddedXml);
        Save(subFolder, "output.hl7", result.Success ? result.Hl7v2Message : "(failed)");
        Save(subFolder, "notes.txt", $"CorrectWhitespaces={correctWhitespaces}{Environment.NewLine}Success={result.Success}{Environment.NewLine}Error={result.Error?.Message}");

        Assert.That(result.Success, Is.True, () => $"CreateFromXml failed: {result.Error?.Message}");
        var obxLine = result.Hl7v2Message.Split('\r').Single(l => l.StartsWith("OBX|", StringComparison.Ordinal));

        if (correctWhitespaces)
            Assert.That(obxLine, Does.Contain("|42.7|"), "CorrectWhitespaces=true should trim/collapse the padded value back to '42.7'.");
        else
            Assert.That(obxLine, Does.Contain("|   42.7   |"), "CorrectWhitespaces=false should preserve the padded value exactly.");
    }

    /// <summary>
    /// CrashOnUnknownTags: EMPIRICAL FINDING, not the documented behaviour. This
    /// option maps to nhapi's <c>ParserOptions.UnexpectedSegmentBehaviour</c>,
    /// which per nhapi's own XML docs governs how the segment *iterator* reacts to
    /// an unexpected segment. That iterator is only used by the pipe-format
    /// parser's traversal logic - nhapi's XML parser instead resolves each XML
    /// element by tag name directly and calls <c>IGroup.AddNonstandardSegment</c>
    /// unconditionally for anything it doesn't recognize (including Z-segments).
    /// So for CreateFromXml (XML -&gt; HL7v2), this flag currently has NO effect:
    /// true and false were verified (here, and via three placements of a
    /// non-standard segment - inside a group, with a non-3-letter tag name, and
    /// at the message root) to produce byte-identical results. This test pins
    /// that actual behaviour rather than asserting the option "works", since it
    /// does not for this input direction - see the task README/CHANGELOG for
    /// whether this is a known limitation.
    /// Confirmed as a direction-specific gap, not a dead nhapi feature: case 8
    /// below drives the same setting from the OTHER direction (HL7 -&gt; XML, the
    /// direction ConvertToXml/"create XML from HL7" uses) and there it genuinely
    /// throws - <c>HL7Exception: Found unknown segment: ZBS</c>.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void CreateFromXmlOption_CrashOnUnknownTags(bool crashOnUnknownTags)
    {
        var caseFolder = CreateCaseFolder(5, "Option_CrashOnUnknownTags");
        var subFolder = Path.Combine(caseFolder, crashOnUnknownTags ? "true" : "false");

        var baseXml = GetBaseXml(); // contains <ZBS>, a nonstandard/unexpected segment

        var result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = baseXml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR, CrashOnUnknownTags = crashOnUnknownTags },
            CancellationToken.None);

        Save(subFolder, "input.xml", baseXml);
        Save(subFolder, "output.hl7", result.Success ? result.Hl7v2Message : "(failed)");
        Save(
            subFolder,
            "notes.txt",
            $"CrashOnUnknownTags={crashOnUnknownTags}{Environment.NewLine}Success={result.Success}{Environment.NewLine}Error={result.Error?.Message}" +
            $"{Environment.NewLine}{Environment.NewLine}NOTE: this option was found to have NO effect on CreateFromXml's XML input " +
            "path - the ZBS segment is added inline either way. See the XML doc comment on this test method for details.");

        // Both branches assert the SAME outcome on purpose - that is the finding.
        Assert.That(result.Success, Is.True, () => $"CreateFromXml failed: {result.Error?.Message}");
        Assert.That(result.Hl7v2Message, Does.Contain("ZBS|"), "The ZBS segment should be present inline regardless of CrashOnUnknownTags.");
    }

    /// <summary>
    /// CrashOnDataLoss: giving a composite/nested field (OBR-16.2, XCN.2) as flat
    /// text instead of the expected nested structure causes nhapi to silently drop
    /// the value. false (default) lets that pass silently; true throws instead.
    /// ThrowErrorOnFailure is pinned to false here so the true/false difference
    /// under test is CrashOnDataLoss alone (Result.Success), not whether the
    /// resulting error is thrown vs returned - that's covered separately below.
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void CreateFromXmlOption_CrashOnDataLoss(bool crashOnDataLoss)
    {
        var caseFolder = CreateCaseFolder(6, "Option_CrashOnDataLoss");
        var subFolder = Path.Combine(caseFolder, crashOnDataLoss ? "true" : "false");

        var baseXml = GetBaseXml();
        var xcn2Pattern = new Regex(@"<XCN\.2>.*?</XCN\.2>", RegexOptions.Singleline);
        Assert.That(xcn2Pattern.IsMatch(baseXml), Is.True, "Test assumes OBR.16 contains a nested <XCN.2><FN.1>Physician</FN.1></XCN.2>.");
        var flatXml = xcn2Pattern.Replace(baseXml, "<XCN.2>Physician</XCN.2>", 1);

        var result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = flatXml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR, CrashOnDataLoss = crashOnDataLoss, ThrowErrorOnFailure = false },
            CancellationToken.None);

        Save(subFolder, "input.xml", flatXml);
        Save(subFolder, "output.hl7", result.Success ? result.Hl7v2Message : "(failed)");
        Save(subFolder, "notes.txt", $"CrashOnDataLoss={crashOnDataLoss}{Environment.NewLine}Success={result.Success}{Environment.NewLine}Error={result.Error?.Message}");

        if (crashOnDataLoss)
        {
            Assert.That(result.Success, Is.False, "CrashOnDataLoss=true should fail when the flat XCN.2 text cannot be placed.");
            Assert.That(result.Error?.Message, Does.Contain("Physician"));
        }
        else
        {
            Assert.That(result.Success, Is.True, () => $"CreateFromXml failed: {result.Error?.Message}");
            var obrLine = result.Hl7v2Message.Split('\r').Single(l => l.StartsWith("OBR|", StringComparison.Ordinal));
            Assert.That(obrLine, Does.Not.Contain("Physician"), "CrashOnDataLoss=false should silently lose the flat-text value instead of failing.");
        }
    }

    /// <summary>
    /// ThrowErrorOnFailure: given a scenario that is guaranteed to fail
    /// (CrashOnDataLoss=true against a flat-text XCN.2, see the CrashOnDataLoss
    /// test above), true throws a .NET exception out of the call, false returns
    /// Result.Success = false instead. (CrashOnUnknownTags=true was tried first as
    /// the error trigger but does not actually fail via the XML input path - see
    /// the CrashOnUnknownTags test - so CrashOnDataLoss is used here instead.)
    /// </summary>
    [TestCase(true)]
    [TestCase(false)]
    public void CreateFromXmlOption_ThrowErrorOnFailure(bool throwErrorOnFailure)
    {
        var caseFolder = CreateCaseFolder(7, "Option_ThrowErrorOnFailure");
        var subFolder = Path.Combine(caseFolder, throwErrorOnFailure ? "true" : "false");

        var baseXml = GetBaseXml();
        var xcn2Pattern = new Regex(@"<XCN\.2>.*?</XCN\.2>", RegexOptions.Singleline);
        Assert.That(xcn2Pattern.IsMatch(baseXml), Is.True);
        var flatXml = xcn2Pattern.Replace(baseXml, "<XCN.2>Physician</XCN.2>", 1);

        var options = new CreateFromXmlDefs.Options
        {
            LineEnding = CreateFromXmlDefs.LineEnding.CR,
            CrashOnDataLoss = true, // guarantees an error, independent of ThrowErrorOnFailure
            ThrowErrorOnFailure = throwErrorOnFailure,
        };

        Save(subFolder, "input.xml", flatXml);

        if (throwErrorOnFailure)
        {
            TestDelegate action = () =>
                CreateFromXmlTask.CreateFromXml(new CreateFromXmlDefs.Input { Xml = flatXml }, options, CancellationToken.None);
            var ex = Assert.Throws<Exception>(action);
            Save(subFolder, "notes.txt", $"ThrowErrorOnFailure=true -> threw {ex!.GetType().Name}: {ex.Message}");
        }
        else
        {
            var result = CreateFromXmlTask.CreateFromXml(new CreateFromXmlDefs.Input { Xml = flatXml }, options, CancellationToken.None);
            Save(subFolder, "notes.txt", $"ThrowErrorOnFailure=false -> Success={result.Success}, Error={result.Error?.Message}");
            Assert.That(result.Success, Is.False, "ThrowErrorOnFailure=false should return Success=false instead of throwing.");
            Assert.That(result.Error, Is.Not.Null);
        }
    }

    // ---------------------------------------------------------------------
    // Case 8: does UnexpectedSegmentBehaviour (CrashOnUnknownTags' underlying
    // nhapi setting) have any effect in the OTHER direction - HL7 -> XML - since
    // it turned out to be a no-op for CreateFromXml's XML -> HL7 direction (case 5)?
    // ConvertToXml.cs itself does not expose this as an option; it always calls
    // `new PipeParser().Parse(normalizedMessage)` with nhapi's own default
    // ParserOptions. This test calls the exact same PipeParser.Parse ->
    // DefaultXMLParser.Encode pipeline ConvertToXml.cs uses, but with each
    // UnexpectedSegmentBehaviour value passed explicitly, to determine whether the
    // setting does anything on this side of the library - i.e. whether it would be
    // worth exposing as an option on ConvertToXml at all.
    // ---------------------------------------------------------------------

    /// <summary>
    /// Uses "Frends.Hl7v2.ConvertToXml" (create XML from HL7) as the test subject,
    /// but bypasses its public Task API (which has no CrashOnUnknownTags-equivalent
    /// option) to call the underlying nhapi <see cref="PipeParser"/> directly - the
    /// same class/call ConvertToXml.cs uses internally - with an explicit
    /// <see cref="ParserOptions.UnexpectedSegmentBehaviour"/> for each of nhapi's
    /// three values, against the same ZBS Z-segment message used elsewhere in this
    /// suite.
    /// </summary>
    [TestCase(UnexpectedSegmentBehaviour.AddInline)]
    [TestCase(UnexpectedSegmentBehaviour.ThrowHl7Exception)]
    [TestCase(UnexpectedSegmentBehaviour.DropToRoot)]
    public void ConvertToXmlPipeParser_UnexpectedSegmentBehaviour_Hl7ToXml(UnexpectedSegmentBehaviour behaviour)
    {
        var caseFolder = CreateCaseFolder(8, "PipeParser_UnexpectedSegmentBehaviour_Hl7ToXml");
        var subFolder = Path.Combine(caseFolder, behaviour.ToString());

        var original = ReadSample("SampleOruWithZSegment.hl7"); // contains the ZBS Z-segment
        // Same normalization Frends.Hl7v2.ConvertToXml.Hl7v2.ConvertToXml applies
        // internally before calling PipeParser.Parse.
        var normalizedMessage = original.Replace("\r\n", "\r").Replace("\n", "\r");

        var parserOptions = new ParserOptions { UnexpectedSegmentBehaviour = behaviour };
        var pipeParser = new PipeParser();

        Save(subFolder, "input.hl7", original);

        try
        {
            var parsedMessage = pipeParser.Parse(normalizedMessage, parserOptions);
            var xml = new DefaultXMLParser().Encode(parsedMessage);

            Save(subFolder, "output.xml", xml);
            Save(
                subFolder,
                "notes.txt",
                $"UnexpectedSegmentBehaviour={behaviour} -> PipeParser.Parse succeeded.{Environment.NewLine}" +
                $"ZBS present in output XML: {xml.Contains("<ZBS>", StringComparison.Ordinal)}");

            if (behaviour == UnexpectedSegmentBehaviour.ThrowHl7Exception)
            {
                Assert.Fail(
                    $"Expected UnexpectedSegmentBehaviour.ThrowHl7Exception to throw on the nonstandard ZBS " +
                    $"segment, but PipeParser.Parse succeeded instead.{Environment.NewLine}Output XML:{Environment.NewLine}{xml}");
            }

            Assert.That(xml, Does.Contain("<ZBS>"), $"Expected the ZBS Z-segment to be present in the XML for behaviour {behaviour}.");
        }
        catch (Exception ex) when (ex is not AssertionException)
        {
            Save(subFolder, "notes.txt", $"UnexpectedSegmentBehaviour={behaviour} -> threw {ex.GetType().Name}: {ex.Message}");

            Assert.That(
                behaviour,
                Is.EqualTo(UnexpectedSegmentBehaviour.ThrowHl7Exception),
                $"PipeParser.Parse threw unexpectedly for UnexpectedSegmentBehaviour.{behaviour}: {ex}");
        }
    }

    // ---------------------------------------------------------------------
    // Case 9: ThrowErrorOnFailure is the one Options property that genuinely
    // exists on BOTH tasks' public API (Frends.Hl7v2.ConvertToXml.Definitions.Options
    // and TjPTestCreateFromXml.Definitions.Options). Case 7 already covers the
    // XML -> HL7 direction (CreateFromXml); this covers the other direction,
    // HL7 -> XML (ConvertToXml), using an empty Hl7v2Message as the error trigger -
    // ConvertToXml.cs throws ArgumentNullException("You must provide an HL7
    // message.") for that input, independent of any parsing edge case.
    // ---------------------------------------------------------------------
    [TestCase(true)]
    [TestCase(false)]
    public void ConvertToXmlOption_ThrowErrorOnFailure(bool throwErrorOnFailure)
    {
        var caseFolder = CreateCaseFolder(9, "Option_ThrowErrorOnFailure_Hl7ToXml");
        var subFolder = Path.Combine(caseFolder, throwErrorOnFailure ? "true" : "false");

        var options = new ConvertToXmlDefs.Options
        {
            LineEnding = ConvertToXmlDefs.LineEnding.LF,
            ThrowErrorOnFailure = throwErrorOnFailure,
        };

        Save(subFolder, "input.hl7", "(empty string - deliberately invalid input)");

        if (throwErrorOnFailure)
        {
            TestDelegate action = () =>
                ConvertToXmlTask.ConvertToXml(new ConvertToXmlDefs.Input { Hl7v2Message = string.Empty }, options, CancellationToken.None);
            var ex = Assert.Throws<Exception>(action);
            Save(subFolder, "notes.txt", $"ThrowErrorOnFailure=true -> threw {ex!.GetType().Name}: {ex.Message}");
        }
        else
        {
            var result = ConvertToXmlTask.ConvertToXml(new ConvertToXmlDefs.Input { Hl7v2Message = string.Empty }, options, CancellationToken.None);
            Save(subFolder, "notes.txt", $"ThrowErrorOnFailure=false -> Success={result.Success}, Error={result.Error?.Message}");
            Assert.That(result.Success, Is.False, "ThrowErrorOnFailure=false should return Success=false instead of throwing.");
            Assert.That(result.Error, Is.Not.Null);
        }
    }

    // ---------------------------------------------------------------------
    // Cases 10-13: real-world HL7v2 messages exported from an actual hospital
    // integration (Cerner Millennium / Syngo Dynamics, via Region Skane),
    // sourced from \\REG.SKANE.SE\RSHem\Hem2\219522\Skrivbord\HL7 and copied
    // into TestData as RealWorld_*.hl7 (byte-for-byte, verified via `cmp`).
    // These are messier than the hand-written samples above - they surface real
    // round-trip gaps in the wild rather than constructed edge cases. All are
    // ISO-8859-1 encoded (MSH-18 declares "8859/1" / "ISO_IR 100"), so they're
    // read via ReadRealWorldSample, not the UTF-8-assuming ReadSample.
    // ---------------------------------------------------------------------

    /// <summary>
    /// A real ORM^O01 (order) message round-trips perfectly with no special
    /// handling required.
    /// </summary>
    [TestCase("SDV_ORM.hl7")]
    public void RealWorld_SDV_ORM_RoundTrip_IsIdentical(string fileName)
    {
        var caseFolder = CreateCaseFolder(10, "RealWorld_SDV_ORM_RoundTrip");
        var (original, normalizedOriginal, roundTripped, xml) = RunRealWorldRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff.Length == 0 ? "(none - identical)" : diff);

        Assert.That(
            roundTripped,
            Is.EqualTo(normalizedOriginal),
            () => $"Real-world ORM message did not round-trip identically:{Environment.NewLine}{diff}");
    }

    /// <summary>
    /// A real DFT^P03 (billing) message is NOT byte-identical on round trip.
    /// Every FT1 line repeats two ICD-10 diagnosis codes separated by '~' and
    /// ends that repeating field with a trailing, empty repetition (a dangling
    /// '~' immediately before the next '|'). That's the same class of loss as
    /// case 2 (trailing empty fields/components stripped on encode) showing up
    /// on a repeating field instead of a whole segment, in real production data.
    /// </summary>
    [TestCase("Syngo_DFT.hl7")]
    public void RealWorld_Syngo_DFT_RoundTrip_DetectsTrailingRepetitionLoss(string fileName)
    {
        var caseFolder = CreateCaseFolder(11, "RealWorld_Syngo_DFT_RoundTrip");
        var (original, normalizedOriginal, roundTripped, xml) = RunRealWorldRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff);

        Assert.That(
            roundTripped,
            Is.Not.EqualTo(normalizedOriginal),
            "Expected this real-world DFT message to be lossy - see the XML doc comment above for why.");

        var originalFt1Lines = normalizedOriginal.Split('\r').Where(l => l.StartsWith("FT1|", StringComparison.Ordinal)).ToList();
        var roundTrippedFt1Lines = roundTripped.Split('\r').Where(l => l.StartsWith("FT1|", StringComparison.Ordinal)).ToList();

        Assert.That(originalFt1Lines, Has.All.Contains("~||"), "Test assumes every original FT1 line has a trailing empty repetition ('~||').");
        Assert.That(roundTrippedFt1Lines, Has.None.Contains("~||"), "Expected the trailing empty repetition to be stripped from every FT1 line on round trip.");
        Assert.That(
            originalFt1Lines.Zip(roundTrippedFt1Lines, (o, rt) => o.Replace("~||", "||") == rt).All(match => match),
            Is.True,
            "Expected each round-tripped FT1 line to equal the original with only the trailing '~' before '||' removed.");
    }

    /// <summary>
    /// A real ORU^R01 (echo report) message shows two DISTINCT, independent loss
    /// patterns:
    /// 1. OBX-1's RP field ends in an empty component ('...URL^^URL^') that gets
    ///    stripped - the same trailing-empty-component pattern as cases 2 and 11.
    /// 2. OBX-2 is a free-text report using literal double-spaces and
    ///    column-alignment padding around '\.br\' line-break escapes. With
    ///    CreateFromXml's default CorrectWhitespaces=true, that padding is
    ///    collapsed/trimmed - the same mechanism as case 4, hitting real report
    ///    text this time.
    /// A second CreateFromXml call with CorrectWhitespaces=false proves cause 2
    /// is avoidable (the free-text padding survives) while cause 1 is not
    /// affected by that option at all - i.e. the two causes are independent.
    /// NOTE (unrelated to this library): the source file's Swedish a/a/o
    /// characters in the free-text report are ALREADY corrupted before this test
    /// ever reads them - byte-traced to the file on disk containing the literal
    /// 3-byte UTF-8 encoding of U+FFFD (EF BF BD) in place of those characters,
    /// most likely from a prior UTF-8/Latin-1 mis-round-trip when the sample was
    /// produced or edited. So the whitespace assertion below deliberately anchors
    /// on an ASCII-only substring either side of the corrupted character, rather
    /// than asserting anything about that character's own identity.
    /// </summary>
    [TestCase("Syngo_ORU2.hl7")]
    public void RealWorld_Syngo_ORU2_RoundTrip_DetectsTwoDistinctLossPatterns(string fileName)
    {
        var caseFolder = CreateCaseFolder(12, "RealWorld_Syngo_ORU2_RoundTrip");
        var (original, normalizedOriginal, roundTripped, xml) = RunRealWorldRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff);

        Assert.That(roundTripped, Is.Not.EqualTo(normalizedOriginal));

        var originalLines = normalizedOriginal.Split('\r');
        var roundTrippedLines = roundTripped.Split('\r');

        // Cause 1: trailing empty component on the RP field.
        var originalRp = originalLines.Single(l => l.StartsWith("OBX|1|RP", StringComparison.Ordinal));
        var roundTrippedRp = roundTrippedLines.Single(l => l.StartsWith("OBX|1|RP", StringComparison.Ordinal));
        Assert.That(originalRp, Does.EndWith("^URL^^URL^"));
        Assert.That(roundTrippedRp, Is.EqualTo(originalRp[..^1]), "Expected only the final trailing '^' to be dropped from the RP field.");

        // Cause 2: whitespace correction on the free-text TX field. Re-run with
        // CorrectWhitespaces=false to prove the double-space padding survives
        // when that option is off (isolating this from cause 1, which persists
        // either way).
        var preservedWhitespaceResult = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = xml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR, CorrectWhitespaces = false },
            CancellationToken.None);
        Assert.That(preservedWhitespaceResult.Success, Is.True, () => $"CreateFromXml failed: {preservedWhitespaceResult.Error?.Message}");
        Save(caseFolder, "output.CorrectWhitespaces-false.hl7", preservedWhitespaceResult.Hl7v2Message);

        var preservedTxLine = preservedWhitespaceResult.Hl7v2Message.TrimEnd('\r').Split('\r').Single(l => l.StartsWith("OBX|2|TX", StringComparison.Ordinal));
        Assert.That(
            preservedTxLine,
            Does.Contain("mottagning  EKOKARDIOGRAFI"),
            "With CorrectWhitespaces=false, the double space in the free-text report should survive.");
        Assert.That(
            roundTrippedRp,
            Is.EqualTo(originalRp[..^1]),
            "Cause 1 (RP trailing component) should still be present even with CorrectWhitespaces=false - it's unrelated to whitespace correction.");
    }

    /// <summary>
    /// A real ORU^R01 message with an embedded PDF report (OBX|2|ED|...||^APPLICATION^PDF^BASE64^&lt;data&gt;,
    /// ~240KB total, almost entirely the base64 payload) round-trips byte-for-byte
    /// identically. This test goes further than the other round-trip cases: it
    /// pulls the base64 PDF out of both the original and round-tripped message
    /// independently, confirms the two payloads are identical, decodes it, WRITES
    /// A REAL .pdf FILE into this run's output folder, and validates the decoded
    /// bytes are a structurally valid PDF (starts with the "%PDF-" magic header,
    /// ends with an "%%EOF" trailer) rather than just "some bytes that happened
    /// to match".
    /// </summary>
    [TestCase("Syngo_ORU_WithPdf.hl7")]
    public void RealWorld_Syngo_ORU_WithPdf_RoundTrip_ExtractsValidPdf(string fileName)
    {
        var caseFolder = CreateCaseFolder(13, "RealWorld_Syngo_ORU_WithPdf_RoundTrip");
        var (original, normalizedOriginal, roundTripped, xml) = RunRealWorldRoundTrip(fileName);
        var diff = BuildLineDiff(normalizedOriginal, roundTripped);

        Save(caseFolder, "input.hl7", original);
        Save(caseFolder, "intermediate.xml", xml);
        Save(caseFolder, "output.hl7", roundTripped);
        Save(caseFolder, "diff.txt", diff.Length == 0 ? "(none - identical)" : diff);

        Assert.That(
            roundTripped,
            Is.EqualTo(normalizedOriginal),
            () => $"Real-world ORU+PDF message did not round-trip identically:{Environment.NewLine}{diff}");

        var base64Pattern = new Regex(@"\^APPLICATION\^PDF\^BASE64\^([A-Za-z0-9+/=]+)", RegexOptions.Singleline);
        var originalMatch = base64Pattern.Match(normalizedOriginal);
        var roundTrippedMatch = base64Pattern.Match(roundTripped);

        Assert.That(originalMatch.Success, Is.True, "Test assumes the original message contains an OBX ED field with an embedded BASE64 PDF.");
        Assert.That(roundTrippedMatch.Success, Is.True, "The round-tripped message no longer contains the embedded BASE64 PDF field.");
        Assert.That(
            roundTrippedMatch.Groups[1].Value,
            Is.EqualTo(originalMatch.Groups[1].Value),
            "The embedded PDF's base64 payload changed during the round trip.");

        var pdfBytes = Convert.FromBase64String(roundTrippedMatch.Groups[1].Value);

        var pdfPath = Path.Combine(caseFolder, "extracted-report.pdf");
        File.WriteAllBytes(pdfPath, pdfBytes);

        var header = Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(8, pdfBytes.Length));
        var tail = Encoding.ASCII.GetString(pdfBytes, Math.Max(0, pdfBytes.Length - 32), Math.Min(32, pdfBytes.Length));

        Assert.That(header, Does.StartWith("%PDF-"), "Decoded file does not start with the PDF magic header.");
        Assert.That(tail, Does.Contain("%%EOF"), "Decoded file does not end with the PDF trailer marker.");

        Save(
            caseFolder,
            "notes.txt",
            $"Embedded PDF extracted from OBX|2|ED field.{Environment.NewLine}" +
            $"Base64 length: {roundTrippedMatch.Groups[1].Value.Length} chars{Environment.NewLine}" +
            $"Decoded PDF size: {pdfBytes.Length} bytes{Environment.NewLine}" +
            $"Header: {header}{Environment.NewLine}" +
            $"Saved to: {pdfPath}");
    }

    // ---------------------------------------------------------------------
    // Case 14: dynamic - one NUnit test case is generated per file currently
    // sitting in HL7Messages/, whatever that number is (4 today, 100+ once more
    // are dropped in - nothing in this test file needs to change to pick them
    // up). Unlike cases 10-13 above (which assert specific, already-understood
    // findings), this is a lenient smoke test over the whole folder: PASS means
    // ConvertToXml and CreateFromXml both completed without throwing or
    // returning Success=false - NOT that the round trip was byte-identical,
    // since a successful-but-lossy round trip is a legitimate, already-
    // documented outcome (see cases 2, 11, 12). Byte-identical-or-not is still
    // recorded per file in that file's diff.txt/notes.txt for follow-up.
    // This is the same check the standalone Frends.Hl7v2.RoundTripRunner
    // console app performs over the same folder, for a quick pass/fail dump
    // outside of `dotnet test`/NUnit.
    // ---------------------------------------------------------------------

    private static IEnumerable<string> AllHl7MessagesInFolder()
    {
        if (!Directory.Exists(Hl7MessagesFolder))
            yield break;

        var files = Directory.EnumerateFiles(Hl7MessagesFolder, "*.hl7")
            .Concat(Directory.EnumerateFiles(Hl7MessagesFolder, "*.txt"))
            .Select(Path.GetFileName)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

        foreach (var name in files)
            yield return name;
    }

    [TestCaseSource(nameof(AllHl7MessagesInFolder))]
    public void HL7MessagesFolder_RoundTrip_DoesNotFail(string fileName)
    {
        var caseFolder = CreateCaseFolder(14, "AllHl7MessagesInFolder");
        var subFolder = Path.Combine(caseFolder, Path.GetFileNameWithoutExtension(fileName));

        var original = ReadRealWorldSample(fileName);
        var normalizedOriginal = Normalize(original);

        var toXmlResult = ConvertToXmlTask.ConvertToXml(
            new ConvertToXmlDefs.Input { Hl7v2Message = original },
            new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
            CancellationToken.None);

        if (!toXmlResult.Success)
        {
            Save(subFolder, "input.hl7", original);
            Save(subFolder, "notes.txt", $"FAILED at ConvertToXml: {toXmlResult.Error?.Message}");
            Assert.Fail($"ConvertToXml failed for '{fileName}': {toXmlResult.Error?.Message}");
            return;
        }

        var toHl7Result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = toXmlResult.Xml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR },
            CancellationToken.None);

        if (!toHl7Result.Success)
        {
            Save(subFolder, "input.hl7", original);
            Save(subFolder, "intermediate.xml", toXmlResult.Xml);
            Save(subFolder, "notes.txt", $"FAILED at CreateFromXml: {toHl7Result.Error?.Message}");
            Assert.Fail($"CreateFromXml failed for '{fileName}': {toHl7Result.Error?.Message}");
            return;
        }

        var roundTripped = toHl7Result.Hl7v2Message.TrimEnd('\r');
        var identical = roundTripped == normalizedOriginal;
        var diff = identical ? "(none - identical)" : BuildLineDiff(normalizedOriginal, roundTripped);

        Save(subFolder, "input.hl7", original);
        Save(subFolder, "intermediate.xml", toXmlResult.Xml);
        Save(subFolder, "output.hl7", roundTripped);
        Save(subFolder, "diff.txt", diff);
        Save(subFolder, "notes.txt", $"PASS (both steps succeeded). Byte-identical round trip: {identical}");
    }

    // ---------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------

    private static (string Original, string NormalizedOriginal, string RoundTripped, string Xml) RunRealWorldRoundTrip(string fileName)
    {
        var original = ReadRealWorldSample(fileName);
        var normalizedOriginal = Normalize(original);

        var toXmlResult = ConvertToXmlTask.ConvertToXml(
            new ConvertToXmlDefs.Input { Hl7v2Message = original },
            new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
            CancellationToken.None);
        Assert.That(toXmlResult.Success, Is.True, () => $"ConvertToXml failed: {toXmlResult.Error?.Message}");

        var toHl7Result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = toXmlResult.Xml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR },
            CancellationToken.None);
        Assert.That(toHl7Result.Success, Is.True, () => $"CreateFromXml failed: {toHl7Result.Error?.Message}");

        var roundTripped = toHl7Result.Hl7v2Message.TrimEnd('\r');

        return (original, normalizedOriginal, roundTripped, toXmlResult.Xml);
    }

    private static string GetBaseXml()
    {
        var original = ReadSample("SampleOruWithZSegment.hl7");
        var result = ConvertToXmlTask.ConvertToXml(
            new ConvertToXmlDefs.Input { Hl7v2Message = original },
            new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
            CancellationToken.None);
        Assert.That(result.Success, Is.True, () => $"ConvertToXml failed: {result.Error?.Message}");
        return result.Xml;
    }

    private static (string Original, string NormalizedOriginal, string RoundTripped, string Xml) RunRoundTrip(string fileName)
    {
        var original = ReadSample(fileName);
        var normalizedOriginal = Normalize(original);

        var toXmlResult = ConvertToXmlTask.ConvertToXml(
            new ConvertToXmlDefs.Input { Hl7v2Message = original },
            new ConvertToXmlDefs.Options { LineEnding = ConvertToXmlDefs.LineEnding.LF },
            CancellationToken.None);

        Assert.That(toXmlResult.Success, Is.True, () => $"ConvertToXml failed: {toXmlResult.Error?.Message}");

        var toHl7Result = CreateFromXmlTask.CreateFromXml(
            new CreateFromXmlDefs.Input { Xml = toXmlResult.Xml },
            new CreateFromXmlDefs.Options { LineEnding = CreateFromXmlDefs.LineEnding.CR },
            CancellationToken.None);

        Assert.That(toHl7Result.Success, Is.True, () => $"CreateFromXml failed: {toHl7Result.Error?.Message}");

        var roundTripped = toHl7Result.Hl7v2Message.TrimEnd('\r');

        return (original, normalizedOriginal, roundTripped, toXmlResult.Xml);
    }

    private static string BuildLineDiff(string original, string roundTripped)
    {
        var originalLines = original.Split('\r');
        var roundTrippedLines = roundTripped.Split('\r');
        var max = Math.Max(originalLines.Length, roundTrippedLines.Length);
        var sb = new StringBuilder();

        for (var i = 0; i < max; i++)
        {
            var o = i < originalLines.Length ? originalLines[i] : "<missing line>";
            var r = i < roundTrippedLines.Length ? roundTrippedLines[i] : "<missing line>";

            if (o == r)
                continue;

            sb.AppendLine($"Line {i + 1}:");
            sb.AppendLine($"  - original:      {o}");
            sb.AppendLine($"  + round-tripped: {r}");
        }

        return sb.Length == 0 ? "(no per-line differences found - check for trailing/whitespace differences)" : sb.ToString();
    }
}
