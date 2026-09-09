namespace Sanare.Core.Schema;

/// <summary>Outcome of validating a JSON document against a schema descriptor.</summary>
public sealed record ValidationResult(bool IsValid, IReadOnlyList<SchemaViolation> Violations);
