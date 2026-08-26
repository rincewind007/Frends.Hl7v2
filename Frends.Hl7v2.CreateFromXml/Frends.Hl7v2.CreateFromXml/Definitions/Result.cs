namespace TjPTestCreateFromXml.Definitions;

/// <summary>
/// Result of the task.
/// </summary>
public class Result
{
    /// <summary>
    /// Indicates if the task completed successfully.
    /// </summary>
    /// <example>true</example>
    public bool Success { get; set; }

    /// <summary>
    /// Message in Hl7v2 format.
    /// </summary>
    /// <example>MSH||CohieCentral|COHIE|Clinical Data Provider</example>
    public string Hl7v2Message { get; set; }

    /// <summary>
    /// Error that occurred during task execution.
    /// </summary>
    /// <example>object { string Message, Exception AdditionalInfo }</example>
    public Error Error { get; set; }
}
