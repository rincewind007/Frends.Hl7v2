using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Frends.Hl7v2.CreateFromXml.Definitions;

/// <summary>
/// Additional parameters.
/// </summary>
public class Options
{
    /// <summary>
    /// Define the line ending of the output.
    /// </summary>
    /// <example>LineEnding.LF</example>
    [DefaultValue(LineEnding.LF)]
    public LineEnding LineEnding { get; set; } = LineEnding.LF;

    /// <summary>
    /// Whether to throw an error on failure.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool ThrowErrorOnFailure { get; set; } = true;

    /// <summary>
    /// Overrides the error message on failure.
    /// </summary>
    /// <example>Custom error message.</example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string ErrorMessageOnFailure { get; set; } = string.Empty;

    /// <summary>
    /// Whether nhapi should normalize whitespace in the XML (e.g. collapse "  " to " ") while parsing.
    /// When disabled, whitespace in text nodes is preserved exactly as it appears in the input XML.
    /// </summary>
    /// <example>true</example>
    [DefaultValue(true)]
    public bool CorrectWhitespaces { get; set; } = true;

    /// <summary>
    /// Whether to throw an error when the XML contains a tag nhapi does not recognize.
    /// When disabled, unknown tags are added to the message inline instead of failing the parse.
    /// </summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool CrashOnUnknownTags { get; set; } = false;

    /// <summary>
    /// Whether to throw an error when nhapi silently fails to place a value from the input XML
    /// into the parsed HL7v2 message. This typically happens when a field is given as flat text
    /// in the XML (e.g. &lt;XCN.2&gt;Doctor&lt;/XCN.2&gt;) but the HL7v2 spec defines it as a
    /// composite/nested type (e.g. &lt;XCN.2&gt;&lt;FN.1&gt;Doctor&lt;/FN.1&gt;&lt;/XCN.2&gt;) -
    /// nhapi itself has no built-in option for this, so when enabled the task detects it by
    /// re-encoding the parsed message back to XML and comparing every value in the original XML
    /// against the round-tripped XML.
    /// </summary>
    /// <example>false</example>
    [DefaultValue(false)]
    public bool CrashOnDataLoss { get; set; } = false;
}
