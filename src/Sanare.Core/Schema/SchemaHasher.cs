using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sanare.Core.Schema;

/// <summary>Builds and hashes canonical JSON Schema contracts for derived descriptors.</summary>
public static class SchemaHasher
{
    public static string Compute(SchemaDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(descriptor.JsonSchema))).ToLowerInvariant();
    }

    public static string CreateCanonicalJson(string name, int version, IReadOnlyList<FieldDescriptor> fields)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("$schema", "https://json-schema.org/draft/2020-12/schema");
            writer.WriteString("title", name);
            writer.WriteNumber("x-sanare-schema-version", version);
            writer.WriteStartObject("properties");
            foreach (var field in fields.OrderBy(static field => field.Name, StringComparer.Ordinal))
            {
                writer.WriteStartObject(field.Name);
                writer.WriteString("type", GetJsonType(field.ClrType));
                if (field.Description is not null)
                {
                    writer.WriteString("description", field.Description);
                }

                if (field.Unit is not null)
                {
                    writer.WriteString("x-sanare-unit", field.Unit);
                }

                writer.WriteString("x-sanare-culture", field.Culture);
                if (field.Hint is not null)
                {
                    writer.WriteString("x-sanare-hint", field.Hint);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
            writer.WriteStartArray("required");
            foreach (var field in fields.Where(static field => field.Required).OrderBy(static field => field.Name, StringComparer.Ordinal))
            {
                writer.WriteStringValue(field.Name);
            }

            writer.WriteEndArray();
            writer.WriteString("type", "object");
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string GetJsonType(Type type) =>
        type == typeof(string) || type == typeof(char) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(DateTimeOffset) ? "string" :
        type == typeof(bool) ? "boolean" :
        type.IsEnum ? "string" :
        type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) || type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong) ? "integer" :
        type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? "number" :
        "object";
}
