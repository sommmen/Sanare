using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Fixtures;
using Xunit;

namespace Sanare.Core.Tests.Fixtures;

/// <summary>Offline replay coverage (docs/features/fixture-corpus.md "Offline mode", AC-012, AC-012b,
/// AC-FIX-011): with <see cref="FixtureOptions.Offline"/> set, requests resolve purely from
/// <see cref="IFixtureCorpus"/> by (sourceId, url, pageRole). The zero-sockets guarantee is structural:
/// <see cref="FixtureCorpus"/> has no <see cref="HttpClient"/>/<see cref="HttpMessageHandler"/> dependency
/// anywhere in its public surface, so it cannot open a socket regardless of these tests; a miss fails with
/// <c>SNR-FIX-001</c>, and captures are refused with <c>SNR-FIX-003</c>.</summary>
public sealed class OfflineReplayTests : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanare-offline-replay-tests", Guid.NewGuid().ToString("N"));

    public OfflineReplayTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private FixtureCorpus CreateOnlineCorpusForSeeding(FakeTimeProvider clock) =>
        new(new FixtureOptions(_root, FullRetentionCount: 3, Offline: false), clock);

    private FixtureCorpus CreateOfflineCorpus(FakeTimeProvider clock) =>
        new(new FixtureOptions(_root, FullRetentionCount: 3, Offline: true), clock);

    [Fact]
    public async Task Offline_run_resolves_a_seeded_fixture_from_disk_with_zero_sockets_opened()
    {
        // AC-012: given offline mode and a fixture for the requested source, the run completes from
        // disk. Seed the corpus while "online" (as a prior capture would have done), then resolve the
        // same (sourceId, url, pageRole) purely through IFixtureCorpus, which has no HTTP dependency.
        var clock = new FakeTimeProvider(Epoch);
        var seedCorpus = CreateOnlineCorpusForSeeding(clock);
        const string sourceId = "lenovo-com";
        const string url = "https://www.lenovo.com/tablets/yoga-tab-gen2";
        const string pageRole = "tablet-product";
        var body = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Data", "lenovo-com", "tablet-product-yoga-tab-gen2.html"));
        var seeded = await seedCorpus.CaptureAsync(new CaptureRequest(url, sourceId, AcquisitionTier.Html, "text/html", new MemoryStream(Encoding.UTF8.GetBytes(body)), PageRole: pageRole));

        var offlineCorpus = CreateOfflineCorpus(clock);
        var resolved = await offlineCorpus.QueryAsync(new FixtureQuery(SourceId: sourceId, Url: url, PageRole: pageRole, Latest: true));
        Assert.Single(resolved);
        var content = await offlineCorpus.GetContentAsync(resolved[0].Id);
        var slice = await offlineCorpus.SliceAsync(resolved[0].Id, new FixtureSliceRequest());

        Assert.NotNull(content);
        Assert.Equal(seeded.Id, resolved[0].Id);
        Assert.Contains("Yoga Tab Gen 2", slice.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_run_fails_with_FixtureNotFound_on_a_miss_and_still_opens_no_socket()
    {
        // AC-012b: given offline mode and no matching fixture, the resolution fails with SNR-FIX-001.
        var clock = new FakeTimeProvider(Epoch);
        var offlineCorpus = CreateOfflineCorpus(clock);

        var resolved = await offlineCorpus.QueryAsync(new FixtureQuery(SourceId: "unknown-source", Url: "https://example.test/missing", PageRole: "tablet-product", Latest: true));
        Assert.Empty(resolved);

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => offlineCorpus.SliceAsync("unknown-source/tablet-product-missing", new FixtureSliceRequest()).AsTask());

        Assert.Equal("SNR-FIX-001", exception.Code);
    }

    [Fact]
    public async Task Offline_run_refuses_a_capture_attempt_because_there_is_nothing_to_capture()
    {
        // AC-FIX-011 / SNR-FIX-003: with acquisition resolving purely from the corpus while offline,
        // a capture attempt is refused rather than silently accepted.
        var clock = new FakeTimeProvider(Epoch);
        var offlineCorpus = CreateOfflineCorpus(clock);
        var request = new CaptureRequest("https://example.test/anything", "lenovo-com", AcquisitionTier.Html, "text/html", new MemoryStream(Encoding.UTF8.GetBytes("<html></html>")), PageRole: "tablet-product");

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => offlineCorpus.CaptureAsync(request).AsTask());

        Assert.Equal("SNR-FIX-003", exception.Code);
    }
}
