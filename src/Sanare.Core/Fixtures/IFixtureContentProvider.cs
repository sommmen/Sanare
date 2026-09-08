namespace Sanare.Core.Fixtures;

/// <summary>Offline content boundary for the v0.1 engine; live acquisition is deliberately out of scope.</summary>
public interface IFixtureContentProvider
{
    bool TryGet(string sourceId, Uri url, out string html);
}
