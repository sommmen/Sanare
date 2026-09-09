using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Sanare.Core.Observability;

/// <summary>Ensures telemetry tags stay within the configured bounded vocabulary.</summary>
public sealed class CardinalityGuard
{
    public const string Other = "other";
    private static readonly HashSet<string> ClosedTagNames = new(StringComparer.Ordinal)
    {
        TagNames.Status, TagNames.Tier, TagNames.Origin, TagNames.Kind, TagNames.StatusClass,
        TagNames.Direction, TagNames.Outcome, TagNames.Classification, TagNames.Layer, TagNames.ModelProfile,
    };

    private readonly ISet<string> _sources;
    private readonly ISet<string> _hosts;
    private readonly ISet<string> _schemas;
    private readonly ISet<string> _fields;
    private readonly ILogger<CardinalityGuard>? _logger;
    private readonly ConcurrentDictionary<string, byte> _reportedTagNames = new(StringComparer.Ordinal);

    public CardinalityGuard(
        IEnumerable<string>? sources = null,
        IEnumerable<string>? hosts = null,
        IEnumerable<string>? schemas = null,
        IEnumerable<string>? fields = null,
        ILogger<CardinalityGuard>? logger = null)
    {
        _sources = ToSet(sources);
        _hosts = ToSet(hosts);
        _schemas = ToSet(schemas);
        _fields = ToSet(fields);
        _logger = logger;
    }

    public string Guard(string tagName, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        if (string.IsNullOrWhiteSpace(value) || IsUnsafe(value) || !IsAllowed(tagName, value))
        {
            Report(tagName);
            return Other;
        }

        return value;
    }

    private static ISet<string> ToSet(IEnumerable<string>? values) =>
        new HashSet<string>(values ?? [], StringComparer.Ordinal);

    private bool IsAllowed(string tagName, string value) => tagName switch
    {
        TagNames.Source => _sources.Count > 0 && _sources.Contains(value) || _sources.Count == 0 && IsClosedValue(value),
        TagNames.Host => _hosts.Count > 0 && _hosts.Contains(value) || _hosts.Count == 0 && IsClosedValue(value),
        TagNames.Schema => _schemas.Count > 0 && _schemas.Contains(value) || _schemas.Count == 0 && IsClosedValue(value),
        TagNames.Field => _fields.Count > 0 && _fields.Contains(value) || _fields.Count == 0 && IsClosedValue(value),
        _ when ClosedTagNames.Contains(tagName) => IsClosedValue(value),
        _ => false,
    };

    private static bool IsUnsafe(string value) =>
        value.Contains("://", StringComparison.Ordinal) || value.Contains('?', StringComparison.Ordinal) ||
        value.Contains('&', StringComparison.Ordinal) || value.Contains('=');

    private static bool IsClosedValue(string value)
    {
        if (value.Length > 64)
            return false;

        // Check that value contains only safe characters: alphanumeric, dash, underscore, dot, or slash.
        // Slash is allowed for field paths like "/Price".
        if (!value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.' or '/'))
            return false;

        // Reject values that look like random IDs (e.g., GUIDs or UUIDs).
        // A value that is mostly or entirely hex digits (0-9a-f) is likely a GUID/UUID
        // and has unbounded cardinality, so reject it even if syntactically valid.
        var hexCharCount = value.Count(c => char.IsDigit(c) || c is >= 'a' and <= 'f' or >= 'A' and <= 'F');
        var hexRatio = (double)hexCharCount / value.Length;
        return hexRatio < 0.75; // Allow if < 75% hex chars; reject if >= 75%
    }

    private void Report(string tagName)
    {
        if (_reportedTagNames.TryAdd(tagName, 0))
        {
            _logger?.LogWarning("SNR-OBS-005: Metric tag {TagName} was replaced with {Replacement}.", tagName, Other);
        }
    }
}
