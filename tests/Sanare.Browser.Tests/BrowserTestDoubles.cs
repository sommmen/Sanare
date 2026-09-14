using Sanare.Abstractions;
using Sanare.Core.Fixtures;

namespace Sanare.Browser.Tests;

internal sealed class RecordingFixtureCorpus : IFixtureCorpus
{
    public List<CaptureRequest> Captured { get; } = [];

    public ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Captured.Add(request);
        var id = $"fixture-{Captured.Count:D4}";
        return ValueTask.FromResult(new FixtureRecord(id, request.SourceId, request.Url, DateTimeOffset.UtcNow,
            request.Tier, request.ContentType, $"{id}.bin", 0, string.Empty, string.Empty, [], [],
            request.PageRole, null, RetentionTier.Full, null));
    }

    public ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default) => ValueTask.FromResult<FixtureContent?>(null);
    public ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default) => ValueTask.FromResult<IReadOnlyList<FixtureRecord>>([]);
    public ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default) => ValueTask.FromResult(new FixtureSlice(fixtureId, string.Empty, 0, []));
    public ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default) => ValueTask.FromResult(new PruneReport([]));
}

internal sealed class FakeTimeProvider(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan delta) => _now += delta;
}