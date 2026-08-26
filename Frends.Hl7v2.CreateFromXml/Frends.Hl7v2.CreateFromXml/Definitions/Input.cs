using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using static TjPTestCreateFromXml.Definitions.Enums;

namespace TjPTestCreateFromXml.Definitions;

/// <summary>
/// Essential parameters.
/// </summary>
public class Input
{
    /// <summary>
    /// The input string containing a message in Xml format.
    /// </summary>
    /// <example>
    /// &lt;QRY_R02 xmlns="urn:hl7-org:v2xml"&gt;
    /// &lt;MSH&gt;
    /// &lt;/MSH&gt;
    /// &lt;/QRY_R02&gt;
    /// </example>
    [DisplayFormat(DataFormatString = "Text")]
    [DefaultValue("")]
    public string Xml { get; set; } = string.Empty;

    /// <summary>
    /// Optional MSH field overrides applied to the output HL7v2 message.
    /// Only specified fields are overwritten - unspecified fields retain their
    /// original values from the input XML.
    /// </summary>
    /// <example>
    /// [ { "Field": "MSH3_SendingApplication", "Value": "NewSender" }, { "Field": "MSH5_ReceivingApplication", "Value": "NewReceiver" } ]
    /// </example>
    public MshOverride[] MshOverrides { get; set; }
}
