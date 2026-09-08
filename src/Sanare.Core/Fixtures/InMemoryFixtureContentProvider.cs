namespace Sanare.Core.Fixtures;

/// <summary>Deterministic in-memory fixture source for local runs and integration tests.</summary>
public sealed class InMemoryFixtureContentProvider : IFixtureContentProvider
{
    private readonly IReadOnlyDictionary<string, string> _fixtures;

    public InMemoryFixtureContentProvider(IReadOnlyDictionary<string, string> fixtures)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        _fixtures = fixtures;
    }

    public bool TryGet(string sourceId, Uri url, out string html) =>
        _fixtures.TryGetValue(Key(sourceId, url), out html!);

    public static string Key(string sourceId, Uri url) => sourceId + "|" + url.AbsoluteUri;
}
