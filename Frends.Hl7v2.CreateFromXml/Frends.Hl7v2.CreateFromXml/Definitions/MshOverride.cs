using static TjPTestCreateFromXml.Definitions.Enums;

namespace TjPTestCreateFromXml.Definitions;

/// <summary>
/// MSH field override: field to overwrite and its new value.
/// </summary>
public class MshOverride
{
    /// <summary>
    /// MSH field to overwrite.
    /// </summary>
    /// <example>MSH3_SendingApplication</example>
    public MshField Field { get; set; }

    /// <summary>
    /// New value for the field.
    /// </summary>
    /// <example>NewSender</example>
    public string Value { get; set; }
}
