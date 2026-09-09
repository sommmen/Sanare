using System.Collections;

namespace Sanare.Core.Observability.Redaction;

/// <summary>Redacts secrets and PII before data is sent to logs, spans, or audit storage.</summary>
public sealed class RedactionPolicy
{
    public const string Redacted = "[redacted]";
    private static readonly HashSet<string> SensitiveHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization", "Cookie", "Set-Cookie",
    };

    private readonly PiiAllowList _allowList;

    public RedactionPolicy(PiiAllowList? allowList = null) => _allowList = allowList ?? new PiiAllowList();

    public string Redact(string value, string? sourceId = null, string? field = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        var redacted = RedactionPatterns.SecretQueryValue().Replace(value, "$1" + Redacted);
        redacted = RedactionPatterns.SensitiveHeaderValue().Replace(redacted, "$1" + Redacted);
        if (!string.IsNullOrWhiteSpace(sourceId) && !string.IsNullOrWhiteSpace(field) && _allowList.Allows(sourceId, field))
        {
            return redacted;
        }

        redacted = RedactionPatterns.Email().Replace(redacted, Redacted);
        redacted = RedactionPatterns.Phone().Replace(redacted, Redacted);
        redacted = RedactionPatterns.Iban().Replace(redacted, Redacted);
        return RedactionPatterns.PostalCode().Replace(redacted, Redacted);
    }

    public object? RedactValue(string name, object? value, string? sourceId = null, string? field = null)
    {
        if (value is null)
        {
            return null;
        }
        if (SensitiveHeaders.Contains(name))
        {
            return Redacted;
        }
        if (value is string text)
        {
            return Redact(text, sourceId, field);
        }
        if (value is IEnumerable<KeyValuePair<string, object?>> pairs)
        {
            return pairs.Select(pair => new KeyValuePair<string, object?>(
                pair.Key,
                RedactValue(pair.Key, pair.Value, sourceId, field))).ToArray();
        }
        if (value is IDictionary dictionary)
        {
            return dictionary.Cast<DictionaryEntry>().Select(entry =>
            {
                var entryName = entry.Key?.ToString() ?? string.Empty;
                return new KeyValuePair<string, object?>(
                    entryName,
                    RedactValue(entryName, entry.Value, sourceId, field));
            }).ToArray();
        }
        if (value is IEnumerable enumerable and not string)
        {
            return enumerable.Cast<object?>().Select(item => RedactValue(name, item, sourceId, field)).ToArray();
        }
        return value;
    }
}
