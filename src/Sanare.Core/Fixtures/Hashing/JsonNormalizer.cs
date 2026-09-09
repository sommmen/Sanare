using System.Text.Json;

namespace Sanare.Core.Fixtures.Hashing;

public sealed class JsonNormalizer(IReadOnlyCollection<string>? volatileKeys = null)
{
    private readonly HashSet<string> _volatileKeys = new(volatileKeys ?? ["requestId", "timestamp", "sessionId", "nonce"], StringComparer.OrdinalIgnoreCase);

    public string Normalize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        using var output = new MemoryStream();
        using var writer = new Utf8JsonWriter(output);
        Write(document.RootElement, writer);
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(output.ToArray());
    }

    private void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().Where(property => !_volatileKeys.Contains(property.Name)).OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) { Write(item, writer); }
                writer.WriteEndArray();
                break;
            default: element.WriteTo(writer); break;
        }
    }
}
