using System.Text.Json;

namespace Sanare.Core.Fixtures;

/// <summary>Reads offline fixture content from files mapped by a committed manifest.</summary>
public sealed class FileFixtureContentProvider : IFixtureContentProvider
{
    private readonly string _fixtureRoot;
    private readonly IReadOnlyDictionary<string, string> _files;

    /// <param name="fixtureRoot">Directory containing the files named by the manifest.</param>
    /// <param name="manifestPath">Path to a JSON manifest with an <c>entries</c> array.</param>
    public FileFixtureContentProvider(string fixtureRoot, string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        _fixtureRoot = Path.GetFullPath(fixtureRoot);
        _files = LoadManifest(manifestPath);
    }

    public bool TryGet(string sourceId, Uri url, out string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentNullException.ThrowIfNull(url);

        if (!_files.TryGetValue(InMemoryFixtureContentProvider.Key(sourceId, url), out var relativePath))
        {
            html = string.Empty;
            return false;
        }

        var path = Path.GetFullPath(Path.Combine(_fixtureRoot, relativePath));
        if (!IsContainedIn(_fixtureRoot, path) || !File.Exists(path))
        {
            html = string.Empty;
            return false;
        }

        html = File.ReadAllText(path);
        return true;
    }

    /// <summary>
    /// Reports whether <paramref name="candidate"/> lies beneath <paramref name="root"/>.
    /// </summary>
    /// <remarks>
    /// A textual prefix comparison cannot answer this correctly: an ordinal-ignore-case check treats
    /// <c>/tmp/foo/secret</c> as inside <c>/tmp/Foo</c>, which is wrong on a case-sensitive filesystem and
    /// lets a manifest entry escape the fixture root. <see cref="Path.GetRelativePath(string, string)"/>
    /// applies the host's own path semantics, and a relative path that stays inside the root can neither
    /// be rooted nor begin with a parent-directory segment.
    /// </remarks>
    private static bool IsContainedIn(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> LoadManifest(string manifestPath)
    {
        try
        {
            using var stream = File.OpenRead(manifestPath);
            var manifest = JsonSerializer.Deserialize<Manifest>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? throw new InvalidDataException($"Fixture manifest '{manifestPath}' is empty.");
            if (manifest.Entries is null)
            {
                throw new InvalidDataException($"Fixture manifest '{manifestPath}' is missing entries.");
            }

            return manifest.Entries.ToDictionary(
                entry => InMemoryFixtureContentProvider.Key(
                    Require(entry.SourceId, nameof(entry.SourceId), manifestPath),
                    new Uri(Require(entry.Url, nameof(entry.Url), manifestPath), UriKind.Absolute)),
                entry => Require(entry.File, nameof(entry.File), manifestPath),
                StringComparer.Ordinal);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Fixture manifest '{manifestPath}' is invalid.", exception);
        }
    }

    private static string Require(string? value, string propertyName, string manifestPath) =>
        !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException($"Fixture manifest '{manifestPath}' has an entry without '{propertyName}'.");

    private sealed class Manifest
    {
        public List<Entry>? Entries { get; init; }
    }

    private sealed class Entry
    {
        public string? SourceId { get; init; }

        public string? Url { get; init; }

        public string? File { get; init; }
    }
}
