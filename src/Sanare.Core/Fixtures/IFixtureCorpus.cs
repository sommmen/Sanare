namespace Sanare.Core.Fixtures;

public interface IFixtureCorpus
{
    ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default);
    ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default);
    ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default);
    ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default);
    ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default);
}
