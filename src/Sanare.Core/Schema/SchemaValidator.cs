namespace Sanare.Core.Schema;

/// <summary>Validates the required-field rule of the v0.1 schema contract.</summary>
public sealed class SchemaValidator : ISchemaValidator
{
    public SchemaValidationResult Validate(SchemaDescriptor schema, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);

        var violations = schema.Fields
            .Where(field => field.Required && (!values.TryGetValue(field.JsonPointer, out var value) || value is null))
            .Select(static field => new SchemaViolation("SNR-SCH-004", field.JsonPointer, "A required field is missing."))
            .ToArray();
        return new SchemaValidationResult(violations.Length == 0, violations);
    }
}
