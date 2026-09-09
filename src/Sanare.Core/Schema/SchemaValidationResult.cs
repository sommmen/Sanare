namespace Sanare.Core.Schema;

/// <summary>Compatibility result for validation of the legacy pointer-value document.</summary>
public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<SchemaViolation> Violations);
