using System.Globalization;

namespace Sanare.Core.Fixtures;

public sealed class FixturePathBuilder
{
    public string BuildId(string sourceId, string? pageRole, DateTimeOffset capturedAt, string contentHash) =>
        $"{sourceId}/{Slug(pageRole ?? "capture")}-{capturedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-{HashPrefix(contentHash)}";

    public string BuildRelativeFile(string sourceId, string? pageRole, DateTimeOffset capturedAt, string contentHash, string contentType) =>
        Path.Combine(Slug(sourceId), $"{Slug(pageRole ?? "capture")}-{capturedAt.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)}-{HashPrefix(contentHash)}.{Extension(contentType)}");

    public static string Extension(string contentType) => contentType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
    {
        "text/html" or "application/xhtml+xml" => "html",
        "application/json" or "text/json" or "application/ld+json" => "json",
        "application/http" => "har",
        _ => "txt",
    };

    private static string HashPrefix(string hash) => hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..15] : hash[..Math.Min(8, hash.Length)];

    /// <summary>Sanitises a fixture path segment so it is safe as a Windows and POSIX filename component:
    /// replaces path separators, whitespace, and reserved filename characters (`:*?"&lt;&gt;|`) with `-`.</summary>
    private static string Slug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch is '/' or '\\' or ' ' or ':' or '*' or '?' or '"' or '<' or '>' or '|' ? '-' : ch);
        }
        return builder.ToString().ToLowerInvariant();
    }
}
