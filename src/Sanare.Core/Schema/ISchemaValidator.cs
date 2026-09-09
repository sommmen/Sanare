using System.Text.Json.Nodes;

namespace Sanare.Core.Schema;

/// <summary>Validates extracted documents against a derived schema.</summary>
public interface ISchemaValidator
{
    /// <summary>Validates a JSON document structurally against the derived schema.</summary>
    ValidationResult Validate(JsonNode document, SchemaDescriptor schema);

    /// <summary>Validates a legacy pointer-value document against the derived schema.</summary>
    SchemaValidationResult Validate(SchemaDescriptor schema, IReadOnlyDictionary<string, object?> values);
}
