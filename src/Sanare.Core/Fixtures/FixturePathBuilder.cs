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

    private static string HashPrefix(string hash)
    {
        var withoutPrefix = hash.StartsWith("sha256:", StringComparison.Ordinal) ? hash[7..] : hash;
        return withoutPrefix[..Math.Min(8, withoutPrefix.Length)];
    }

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "con", "prn", "aux", "nul",
        "com1", "com2", "com3", "com4", "com5", "com6", "com7", "com8", "com9",
        "lpt1", "lpt2", "lpt3", "lpt4", "lpt5", "lpt6", "lpt7", "lpt8", "lpt9",
    };

    /// <summary>Sanitises a fixture path segment so it is safe as a Windows and POSIX filename component:
    /// replaces path separators, whitespace, reserved filename characters (`:*?"&lt;&gt;|`), and ASCII
    /// control characters with `-`; rewrites a resulting `.`/`..` segment so it can never be interpreted
    /// as a directory-traversal component by <see cref="Path.Combine(string, string)"/>; and rewrites a
    /// reserved Windows device name (`con`, `prn`, `aux`, `nul`, `com1`-`com9`, `lpt1`-`lpt9`) so it can
    /// never fail file/directory creation on Windows.</summary>
    private static string Slug(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch is '/' or '\\' or ' ' or ':' or '*' or '?' or '"' or '<' or '>' or '|' || char.IsControl(ch) ? '-' : ch);
        }
        var slug = builder.ToString().ToLowerInvariant();
        if (slug is "." or "..") { return "capture-" + slug.Length; }
        return ReservedWindowsNames.Contains(slug) ? slug + "-fixture" : slug;
    }
}
