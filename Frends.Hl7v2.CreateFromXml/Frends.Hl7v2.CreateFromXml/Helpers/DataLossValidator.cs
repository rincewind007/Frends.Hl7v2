using System.Collections.Generic;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using NHapi.Base;

namespace Frends.Hl7v2.CreateFromXml.Helpers;

/// <summary>
/// nhapi itself does not offer an option to fail when it cannot place a value from an input XML
/// element into the parsed HL7v2 message (e.g. because the XML gives flat text for a field the
/// spec defines as a composite/nested type). This is detected here instead, purely via nhapi's
/// public parse/encode API: the parsed message is re-encoded back to XML and every leaf value in
/// the original input is checked against the round-tripped XML.
/// </summary>
public static class DataLossValidator
{
    /// <summary>
    /// Throws an <c>HL7Exception</c> listing any value(s) present in <paramref name="inputXml"/>
    /// that are missing from <paramref name="roundTrippedXml"/>.
    /// </summary>
    /// <param name="inputXml">The original XML that was parsed.</param>
    /// <param name="roundTrippedXml">The parsed message, re-encoded back to XML via the same nhapi <c>XMLParser</c>.</param>
    /// <param name="correctWhitespace">Should match <c>Options.CorrectWhitespaces</c> so whitespace nhapi legitimately normalized is not mistaken for lost data.</param>
    public static void ThrowIfDataWasLost(string inputXml, XmlDocument roundTrippedXml, bool correctWhitespace)
    {
        var inputValues = GetLeafValues(XDocument.Parse(inputXml).Root, correctWhitespace).ToList();
        var outputValues = GetLeafValues(XDocument.Parse(roundTrippedXml.OuterXml).Root, correctWhitespace).ToList();

        var missing = new List<string>();
        foreach (var value in inputValues)
        {
            var index = outputValues.IndexOf(value);
            if (index >= 0)
                outputValues.RemoveAt(index);
            else
                missing.Add(value);
        }

        if (missing.Count > 0)
        {
            throw new HL7Exception(
                "nhapi could not place the following value(s) from the input XML into the HL7v2 message: " +
                string.Join(", ", missing.Select(value => $"\"{value}\"")) +
                ". This usually means the XML gives a flat text value for a field that the HL7v2 spec " +
                "defines as a composite/nested type (e.g. a Family Name given as <XCN.2>Doctor</XCN.2> " +
                "instead of <XCN.2><FN.1>Doctor</FN.1></XCN.2>).");
        }
    }

    private static IEnumerable<string> GetLeafValues(XElement root, bool correctWhitespace)
    {
        if (root == null)
            yield break;

        foreach (var element in root.DescendantsAndSelf())
        {
            if (element.HasElements)
                continue;

            var value = correctWhitespace ? CollapseWhitespace(element.Value) : element.Value?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(value))
                yield return value;
        }
    }

    // Mirrors NHapi.Base.Parser.XMLParser.RemoveWhitespace exactly, so this comparison agrees with
    // whatever whitespace correction nhapi itself already applied when CorrectWhitespaces is enabled.
    private static string CollapseWhitespace(string input)
    {
        input = (input ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');

        while (input.Contains("  "))
            input = input.Replace("  ", " ");

        return input.Trim();
    }
}
