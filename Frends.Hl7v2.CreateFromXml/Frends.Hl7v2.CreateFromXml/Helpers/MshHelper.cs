using System.Collections.Generic;
using TjPTestCreateFromXml.Definitions;
using NHapi.Base.Model;
using NHapi.Base.Util;
using static TjPTestCreateFromXml.Definitions.Enums;

namespace TjPTestCreateFromXml.Helpers;

internal static class MshHelper
{
    private static readonly Dictionary<MshField, int> FieldNumbers = new()
    {
        { MshField.MSH1_FieldSeparator, 1 },
        { MshField.MSH2_EncodingCharacters, 2 },
        { MshField.MSH3_SendingApplication, 3 },
        { MshField.MSH4_SendingFacility, 4 },
        { MshField.MSH5_ReceivingApplication, 5 },
        { MshField.MSH6_ReceivingFacility, 6 },
        { MshField.MSH7_DateTimeOfMessage, 7 },
        { MshField.MSH8_Security, 8 },
        { MshField.MSH9_MessageType, 9 },
        { MshField.MSH10_MessageControlId, 10 },
        { MshField.MSH11_ProcessingId, 11 },
        { MshField.MSH12_VersionId, 12 },
        { MshField.MSH13_SequenceNumber, 13 },
        { MshField.MSH14_ContinuationPointer, 14 },
        { MshField.MSH15_AcceptAcknowledgmentType, 15 },
    };

    internal static void ApplyOverrides(IMessage message, MshOverride[] overrides)
    {
        if (overrides == null || overrides.Length == 0)
            return;

        var terser = new Terser(message);
        foreach (var item in overrides)
        {
            var fieldNumber = FieldNumbers[item.Field];
            terser.Set($"/MSH-{fieldNumber}", item.Value);
        }
    }
}
