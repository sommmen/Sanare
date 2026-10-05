using System.Text.Json;
using Sanare.Core.Fixtures;

namespace Sanare.Core.Tests.Fixtures;

public sealed class FileFixtureContentProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanare-file-fixtures", Guid.NewGuid().ToString("N"));

    public FileFixtureContentProviderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void TryGet_reads_mapped_fixture_file()
    {
        var url = new Uri("https://example.test/tablets/yoga");
        File.WriteAllText(Path.Combine(_root, "yoga.html"), "<h1>Yoga</h1>");
        var manifest = WriteManifest(new[] { new { sourceId = "example/tablets", url = url.AbsoluteUri, file = "yoga.html" } });
        var provider = new FileFixtureContentProvider(_root, manifest);

        var found = provider.TryGet("example/tablets", url, out var html);

        Assert.True(found);
        Assert.Equal("<h1>Yoga</h1>", html);
    }

    [Fact]
    public void TryGet_returns_false_when_no_manifest_entry_matches()
    {
        var manifest = WriteManifest(Array.Empty<object>());
        var provider = new FileFixtureContentProvider(_root, manifest);

        var found = provider.TryGet("example/tablets", new Uri("https://example.test/missing"), out var html);

        Assert.False(found);
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void TryGet_returns_false_when_mapped_file_is_missing()
    {
        var url = new Uri("https://example.test/tablets/yoga");
        var manifest = WriteManifest(new[] { new { sourceId = "example/tablets", url = url.AbsoluteUri, file = "missing.html" } });
        var provider = new FileFixtureContentProvider(_root, manifest);

        var found = provider.TryGet("example/tablets", url, out var html);

        Assert.False(found);
        Assert.Equal(string.Empty, html);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private string WriteManifest<T>(T entries)
    {
        var path = Path.Combine(_root, "manifest.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new { entries }));
        return path;
    }
}
