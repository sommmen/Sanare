using System.Text.Json.Nodes;

namespace Sanare.Core.Schema;

/// <summary>Validates derived schema fields without stopping at the first violation.</summary>
public sealed class SchemaValidator : ISchemaValidator
{
    public ValidationResult Validate(JsonNode document, SchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(schema);

        var violations = new List<SchemaViolation>();
        foreach (var field in schema.Fields)
        {
            ValidateField(document, field, violations);
        }

        return new ValidationResult(violations.Count == 0, violations);
    }

    public SchemaValidationResult Validate(SchemaDescriptor schema, IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(values);

        var violations = schema.Fields
            .Where(field => field.Required && (!values.TryGetValue(field.JsonPointer, out var value) || value is null))
            .Select(static field => new SchemaViolation(field.JsonPointer, "required", "A required field is missing."))
            .ToArray();
        return new SchemaValidationResult(violations.Length == 0, violations);
    }

    private static void ValidateField(JsonNode document, FieldDescriptor field, List<SchemaViolation> violations)
    {
        var nodes = ResolveNodes(document, field.JsonPointer).ToArray();
        if (nodes.Length == 0 || nodes.All(static node => node is null))
        {
            if (field.Required)
            {
                violations.Add(new SchemaViolation(field.JsonPointer, "required", "A required field is missing."));
            }

            return;
        }

        foreach (var node in nodes.Where(static node => node is not null))
        {
            ValidateNode(node!, field, violations);
        }
    }

    private static void ValidateNode(JsonNode node, FieldDescriptor field, ICollection<SchemaViolation> violations)
    {
        if (!MatchesType(node, field.ClrType))
        {
            violations.Add(new SchemaViolation(field.JsonPointer, "type", $"Expected a value compatible with '{field.ClrType.Name}'."));
            return;
        }

        if (field.ClrType.IsEnum && node is JsonValue enumNode && enumNode.TryGetValue<string>(out var enumText) &&
            !Enum.GetNames(field.ClrType).Contains(enumText, StringComparer.OrdinalIgnoreCase))
        {
            violations.Add(new SchemaViolation(field.JsonPointer, "enum", $"'{field.Name}' is not a defined '{field.ClrType.Name}' value."));
        }

        if (node is JsonArray array)
        {
            if (array.Count == 0 && field.Required)
            {
                violations.Add(new SchemaViolation(field.JsonPointer, "minItems", "A required collection must contain at least one item."));
            }
        }
    }

    private static bool MatchesType(JsonNode node, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (node is JsonArray)
        {
            return type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>));
        }

        if (node is JsonObject)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>);
        }

        if (node is not JsonValue value)
        {
            return false;
        }

        return (type == typeof(string) || type == typeof(Uri) || type.IsEnum) ? value.TryGetValue<string>(out _)
            : type == typeof(bool) ? value.TryGetValue<bool>(out _)
            : type == typeof(int)
                    ? value.TryGetValue<int>(out _)
                    : type == typeof(long)
                        ? value.TryGetValue<long>(out _)
                        : type == typeof(decimal)
                            ? value.TryGetValue<decimal>(out _)
                            : type == typeof(DateTime)
                                ? value.TryGetValue<DateTime>(out _) || value.TryGetValue<string>(out _)
                                : type == typeof(DateOnly)
                                    ? value.TryGetValue<DateOnly>(out _) || value.TryGetValue<string>(out _)
                                    : false;
    }

    private static IEnumerable<JsonNode?> ResolveNodes(JsonNode node, string pointer)
    {
        var segments = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static segment => segment.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal));
        IEnumerable<JsonNode?> nodes = [node];
        foreach (var segment in segments)
        {
            nodes = nodes.SelectMany(current => current switch
            {
                JsonObject obj when obj.TryGetPropertyValue(segment, out var child) => [child],
                JsonArray array when int.TryParse(segment, out var index) && index >= 0 && index < array.Count => [array[index]],
                JsonArray array => array.Select(item => item?[segment]),
                _ => Array.Empty<JsonNode?>(),
            });
        }

        return nodes;
    }
}
