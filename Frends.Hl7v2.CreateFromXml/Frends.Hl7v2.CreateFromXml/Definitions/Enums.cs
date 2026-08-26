namespace TjPTestCreateFromXml.Definitions
{
    /// <summary>
    /// Enums used in the task.
    /// </summary>
    public class Enums
    {
        public enum MshField
        {
#pragma warning disable SA1602 // Enumeration items should be documented
            MSH1_FieldSeparator,
            MSH2_EncodingCharacters,
            MSH3_SendingApplication,
            MSH4_SendingFacility,
            MSH5_ReceivingApplication,
            MSH6_ReceivingFacility,
            MSH7_DateTimeOfMessage,
            MSH8_Security,
            MSH9_MessageType,
            MSH10_MessageControlId,
            MSH11_ProcessingId,
            MSH12_VersionId,
            MSH13_SequenceNumber,
            MSH14_ContinuationPointer,
            MSH15_AcceptAcknowledgmentType,
#pragma warning restore SA1602 // Enumeration items should be documented
        }
    }
}
