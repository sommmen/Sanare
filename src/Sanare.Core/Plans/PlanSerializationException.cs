namespace Sanare.Core.Plans;

/// <summary>
/// Raised when a plan document cannot be read (malformed JSON, missing required field, or an
/// unrecognised enum value). Corresponds to error code <c>SNR-PLAN-001</c>; see
/// docs/features/extraction-plan-model.md ("Error Handling").
/// </summary>
public sealed class PlanSerializationException : Exception
{
    public PlanSerializationException(string message)
        : base(message)
    {
    }

    public PlanSerializationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
