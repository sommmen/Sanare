namespace Sanare.Core.Schema;

/// <summary>Validates a plan's field observations against its derived schema.</summary>
public interface ISchemaValidator
{
    SchemaValidationResult Validate(SchemaDescriptor schema, IReadOnlyDictionary<string, object?> values);
}
