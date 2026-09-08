using System.Globalization;

namespace Sanare.Core.Resolution;

/// <summary>
/// Parses <c>approved/{source-id}/{schema-name}@{schemaVersion}/{n}</c> tag names and orders them
/// numerically (never lexicographically — <c>/10</c> must outrank <c>/9</c>). See
/// docs/features/plan-resolver.md ("Warm index and tag resolution").
/// </summary>
public static class ApprovalTagParser
{
    private const string Prefix = "approved/";

    /// <summary>Builds the tag-name prefix for a (sourceId, schemaName, schemaVersion) triple.</summary>
    public static string BuildPrefix(string sourceId, string schemaName, int schemaVersion) =>
        $"{Prefix}{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}/";

    /// <summary>Attempts to parse an approval tag's monotonic suffix number.</summary>
    public static bool TryParseNumber(string tagName, string prefix, out int number)
    {
        number = 0;
        if (!tagName.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(tagName.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}
