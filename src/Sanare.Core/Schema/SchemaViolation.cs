namespace Sanare.Core.Schema;

/// <summary>A structural or required-value schema validation failure.</summary>
public sealed record SchemaViolation(string JsonPointer, string Keyword, string Message)
{
    /// <summary>Compatibility alias for callers that previously consumed diagnostic codes.</summary>
    public string Code => Keyword == "required" ? "SNR-SCH-004" : "SNR-SCH-002";
}
