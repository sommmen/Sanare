namespace Sanare.Core.Schema;

/// <summary>Outcome of validating extracted values against a schema descriptor.</summary>
public sealed record SchemaValidationResult(bool IsValid, IReadOnlyList<SchemaViolation> Violations);

/// <summary>A structural or required-value schema validation failure.</summary>
public sealed record SchemaViolation(string Code, string JsonPointer, string Message);
