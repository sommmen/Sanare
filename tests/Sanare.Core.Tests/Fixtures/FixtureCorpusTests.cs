using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Fixtures;
using Xunit;

namespace Sanare.Core.Tests.Fixtures;

/// <summary>End-to-end coverage of <see cref="FixtureCorpus"/>: capture/dedupe via both hashes, size
/// boundary at 8 MB (AC-FIX-010), manifest atomicity (AC-FIX-005), corrupt manifest (AC-FIX-006),
/// orphaned entry (AC-FIX-007), concurrent capture stress (AC-012/AC-FIX-012), replay of the Lenovo
/// fixtures, and <see cref="FixturePathBuilder"/> naming (AC-FIX-016).</summary>
public sealed class FixtureCorpusTests : IDisposable
{
    private static readonly DateTimeOffset Epoch = new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sanare-fixture-corpus-tests", Guid.NewGuid().ToString("N"));

    public FixtureCorpusTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) { Directory.Delete(_root, recursive: true); }
    }

    private FixtureCorpus CreateCorpus(FakeTimeProvider? clock = null, int fullRetentionCount = 3, bool offline = false) =>
        new(new FixtureOptions(_root, fullRetentionCount, offline), clock ?? new FakeTimeProvider(Epoch));

    private static CaptureRequest Capture(string sourceId, string body, string? pageRole = "product", string contentType = "text/html", string url = "https://example.test/page") =>
        new(url, sourceId, AcquisitionTier.Html, contentType, new MemoryStream(Encoding.UTF8.GetBytes(body)), PageRole: pageRole);

    [Fact]
    public async Task CaptureAsync_dedupes_a_second_capture_that_differs_only_by_a_volatile_attribute()
    {
        // AC-FIX-001: only a nonce attribute differs => deduped via normalisedHash, no new file, manifest count unchanged.
        var corpus = CreateCorpus();
        var first = await corpus.CaptureAsync(Capture("lenovo-com", "<main nonce=\"aaa\"><h1>Tablet</h1></main>"));
        var second = await corpus.CaptureAsync(Capture("lenovo-com", "<main nonce=\"bbb\"><h1>Tablet</h1></main>"));

        Assert.Equal(first.Id, second.Id);
        var all = await corpus.QueryAsync(new FixtureQuery(SourceId: "lenovo-com"));
        Assert.Single(all);
    }

    [Fact]
    public async Task CaptureAsync_retains_both_captures_when_the_real_content_changes()
    {
        // AC-FIX-002: a genuine price change produces a new fixture; both are retained.
        var corpus = CreateCorpus();
        await corpus.CaptureAsync(Capture("lenovo-com", "<main><span class=\"price\">299.00</span></main>"));
        await corpus.CaptureAsync(Capture("lenovo-com", "<main><span class=\"price\">319.00</span></main>"));

        var all = await corpus.QueryAsync(new FixtureQuery(SourceId: "lenovo-com"));
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task CaptureAsync_updates_LastSeenAt_on_dedupe_without_bumping_CapturedAt()
    {
        var clock = new FakeTimeProvider(Epoch);
        var corpus = CreateCorpus(clock);
        var first = await corpus.CaptureAsync(Capture("lenovo-com", "<main><h1>Tablet</h1></main>"));
        clock.Advance(TimeSpan.FromMinutes(5));
        var second = await corpus.CaptureAsync(Capture("lenovo-com", "<main><h1>Tablet</h1></main>"));

        Assert.Equal(Epoch, first.CapturedAt);
        Assert.Equal(Epoch, second.CapturedAt);
        Assert.Equal(Epoch + TimeSpan.FromMinutes(5), second.LastSeenAt);
    }

    [Fact]
    public async Task CaptureAsync_accepts_a_body_of_exactly_8MB_intact_and_unflagged()
    {
        // AC-FIX-010 boundary: 8 MB exactly is accepted intact (not truncated).
        var corpus = CreateCorpus();
        var body = new byte[FixtureOptions.MaximumFixtureBytes];
        Array.Fill(body, (byte)'a');
        var request = new CaptureRequest("https://example.test/large", "lenovo-com", AcquisitionTier.Html, "text/plain", new MemoryStream(body), PageRole: "large");

        var record = await corpus.CaptureAsync(request);

        Assert.False(record.IsTruncated);
        Assert.Equal(FixtureOptions.MaximumFixtureBytes, record.Bytes);
    }

    [Fact]
    public async Task CaptureAsync_truncates_and_flags_a_body_larger_than_8MB()
    {
        // AC-FIX-010: a 9 MB response is truncated to 8 MB and flagged IsTruncated so it can be
        // rejected as an authoring input by callers/CI gating on that flag.
        var corpus = CreateCorpus();
        var body = new byte[FixtureOptions.MaximumFixtureBytes + 1024 * 1024];
        Array.Fill(body, (byte)'b');
        var request = new CaptureRequest("https://example.test/oversized", "lenovo-com", AcquisitionTier.Html, "text/plain", new MemoryStream(body), PageRole: "oversized");

        var record = await corpus.CaptureAsync(request);

        Assert.True(record.IsTruncated);
        Assert.Equal(FixtureOptions.MaximumFixtureBytes, record.Bytes);
    }

    [Fact]
    public async Task CaptureAsync_is_refused_while_offline()
    {
        // AC-FIX-011 / SNR-FIX-003: capture requested while offline mode is on is refused.
        var corpus = CreateCorpus(offline: true);

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => corpus.CaptureAsync(Capture("lenovo-com", "<html></html>")).AsTask());

        Assert.Equal("SNR-FIX-003", exception.Code);
    }

    [Fact]
    public async Task GetContentAsync_returns_null_when_the_fixture_id_is_unknown()
    {
        var corpus = CreateCorpus();

        var content = await corpus.GetContentAsync("does-not-exist/anywhere");

        Assert.Null(content);
    }

    [Fact]
    public async Task GetContentAsync_fails_loudly_when_the_manifest_references_a_file_missing_on_disk()
    {
        // AC-FIX-007: a fixture file listed in the manifest but missing on disk fails GetContentAsync
        // with SNR-FIX-002, not a silent null.
        var corpus = CreateCorpus();
        var record = await corpus.CaptureAsync(Capture("lenovo-com", "<html><body>orphan</body></html>"));
        File.Delete(Path.Combine(_root, "fixtures", record.File));

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => corpus.GetContentAsync(record.Id).AsTask());

        Assert.Equal("SNR-FIX-002", exception.Code);
    }

    [Fact]
    public async Task GetContentAsync_fails_loudly_when_the_stored_bytes_no_longer_match_the_content_hash()
    {
        // Corrupt fixture ⇒ loud failure (SNR-FIX-002), never a silent skip.
        var corpus = CreateCorpus();
        var record = await corpus.CaptureAsync(Capture("lenovo-com", "<html><body>original</body></html>"));
        await File.WriteAllTextAsync(Path.Combine(_root, "fixtures", record.File), "<html><body>tampered</body></html>");

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => corpus.GetContentAsync(record.Id).AsTask());

        Assert.Equal("SNR-FIX-002", exception.Code);
    }

    [Fact]
    public async Task Startup_fails_loudly_when_the_manifest_file_contains_invalid_JSON()
    {
        // AC-FIX-006: initialisation fails with SNR-FIX-002 naming the file; the corpus does not
        // silently start empty.
        var fixturesDir = Path.Combine(_root, "fixtures");
        Directory.CreateDirectory(fixturesDir);
        var manifestPath = Path.Combine(fixturesDir, "manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ this is not valid json");
        var corpus = CreateCorpus();

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => corpus.QueryAsync(new FixtureQuery()).AsTask());

        Assert.Equal("SNR-FIX-002", exception.Code);
        Assert.Contains(manifestPath, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_write_survives_a_crash_between_temp_write_and_move()
    {
        // AC-FIX-005: a manifest write interrupted between temp-write and move must leave the
        // original manifest valid and parseable. Simulate the crash by capturing once (writing a
        // valid manifest), then deleting only a leftover ".tmp-*" file that a real crash would leave
        // behind, and confirm the real manifest is still intact and loadable.
        var corpus = CreateCorpus();
        var record = await corpus.CaptureAsync(Capture("lenovo-com", "<html><body>first</body></html>"));

        var fixturesDir = Path.Combine(_root, "fixtures");
        var leftoverTemp = Path.Combine(fixturesDir, "manifest.json.tmp-" + Guid.NewGuid().ToString("N"));
        await File.WriteAllTextAsync(leftoverTemp, "{ truncated during simulated crash");

        var store = new FixtureManifestStore(_root);
        var manifest = await store.LoadAsync();

        Assert.Single(manifest.Fixtures);
        Assert.Equal(record.Id, manifest.Fixtures[0].Id);
        File.Delete(leftoverTemp);
    }

    [Fact]
    public async Task Concurrent_captures_across_multiple_sources_leave_the_manifest_internally_consistent()
    {
        // AC-012 / AC-FIX-012: 200 concurrent captures across 4 sources; the manifest is internally
        // consistent, entry count equals distinct captures, no torn JSON. Fast and deterministic:
        // no wall-clock sleeps, no network, a FakeTimeProvider supplies timestamps.
        var clock = new FakeTimeProvider(Epoch);
        var corpus = CreateCorpus(clock);
        const int total = 200;
        var sources = new[] { "lenovo-com", "bol-com", "coolblue-com", "mediamarkt-com" };

        var tasks = Enumerable.Range(0, total).Select(index =>
        {
            var sourceId = sources[index % sources.Length];
            var body = $"<html><body>page {index}</body></html>";
            return corpus.CaptureAsync(Capture(sourceId, body, pageRole: $"page-{index}")).AsTask();
        });
        var results = await Task.WhenAll(tasks);

        Assert.Equal(total, results.Select(record => record.Id).Distinct().Count());
        var manifest = await new FixtureManifestStore(_root).LoadAsync();
        Assert.Equal(total, manifest.Fixtures.Count);
        foreach (var record in manifest.Fixtures)
        {
            Assert.True(File.Exists(Path.Combine(_root, "fixtures", record.File)), $"missing file for {record.Id}");
        }
    }

    [Fact]
    public async Task SliceAsync_bounds_the_excerpt_and_reports_the_elided_character_count()
    {
        // AC-FIX-013: a slice request with 4000 characters of context on a large page returns a
        // bounded excerpt and reports the elided character count.
        var corpus = CreateCorpus();
        var padding = new string('x', 480_000);
        var body = $"<html><body>{padding}<span id=\"target\">FOUND</span>{padding}</body></html>";
        var record = await corpus.CaptureAsync(Capture("lenovo-com", body, pageRole: "large-page"));

        var slice = await corpus.SliceAsync(record.Id, new FixtureSliceRequest(Selector: "#target"));

        Assert.True(slice.Content.Length <= 4000 + "<span id=\"target\">FOUND</span>".Length + Environment.NewLine.Length);
        Assert.Contains("FOUND", slice.Content, StringComparison.Ordinal);
        Assert.True(slice.ElidedCharacterCount > 0);
        Assert.Empty(slice.Diagnostics);
    }

    [Fact]
    public async Task SliceAsync_clamps_a_40000_character_request_to_the_32000_hard_cap_with_a_warning()
    {
        // AC-FIX-014: a slice request asking for 40000 characters is clamped to the 32000 hard cap
        // with a warning diagnostic.
        var corpus = CreateCorpus();
        var body = "<html><body>" + new string('y', 100_000) + "</body></html>";
        var record = await corpus.CaptureAsync(Capture("lenovo-com", body, pageRole: "huge-page"));

        var slice = await corpus.SliceAsync(record.Id, new FixtureSliceRequest(Offset: 50_000, ContextCharacters: 40_000));

        Assert.True(slice.Content.Length <= 32_000);
        var diagnostic = Assert.Single(slice.Diagnostics);
        Assert.Equal("SNR-FIX-004", diagnostic.Code);
    }

    [Fact]
    public async Task SliceAsync_fails_with_FixtureNotFound_when_the_fixture_id_is_unknown()
    {
        var corpus = CreateCorpus();

        var exception = await Assert.ThrowsAsync<FixtureCorpusException>(() => corpus.SliceAsync("missing/fixture", new FixtureSliceRequest()).AsTask());

        Assert.Equal("SNR-FIX-001", exception.Code);
    }

    [Fact]
    public async Task FixturePathBuilder_file_name_contains_the_page_role_and_a_readable_timestamp()
    {
        // AC-FIX-016: the on-disk file name contains the page role and a readable timestamp.
        var corpus = CreateCorpus();
        var record = await corpus.CaptureAsync(Capture("lenovo-com", "<html><body>ok</body></html>", pageRole: "tablet-lister"));

        Assert.Contains("tablet-lister", record.File, StringComparison.Ordinal);
        Assert.Contains(Epoch.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'"), record.File, StringComparison.Ordinal);
        Assert.Contains("tablet-lister", record.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Replay_of_the_Lenovo_lister_and_product_fixtures_round_trips_through_the_corpus()
    {
        // End-to-end replay of the Lenovo lister and product fixtures via CaptureAsync/GetContentAsync/QueryAsync.
        var clock = new FakeTimeProvider(Epoch);
        var corpus = CreateCorpus(clock);
        var dataDir = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Data", "lenovo-com");
        var lister1 = await File.ReadAllTextAsync(Path.Combine(dataDir, "tablet-lister-page1.html"));
        var lister2 = await File.ReadAllTextAsync(Path.Combine(dataDir, "tablet-lister-page2.html"));
        var product = await File.ReadAllTextAsync(Path.Combine(dataDir, "tablet-product-yoga-tab-gen2.html"));

        var listerRecord1 = await corpus.CaptureAsync(Capture("lenovo-com", lister1, pageRole: "tablet-lister", url: "https://www.lenovo.com/tablets?page=1"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var listerRecord2 = await corpus.CaptureAsync(Capture("lenovo-com", lister2, pageRole: "tablet-lister", url: "https://www.lenovo.com/tablets?page=2"));
        clock.Advance(TimeSpan.FromSeconds(1));
        var productRecord = await corpus.CaptureAsync(Capture("lenovo-com", product, pageRole: "tablet-product", url: "https://www.lenovo.com/tablets/yoga-tab-gen2"));

        var replayedLister1 = await corpus.GetContentAsync(listerRecord1.Id);
        var replayedLister2 = await corpus.GetContentAsync(listerRecord2.Id);
        var replayedProduct = await corpus.GetContentAsync(productRecord.Id);

        Assert.NotNull(replayedLister1);
        Assert.NotNull(replayedLister2);
        Assert.NotNull(replayedProduct);
        Assert.Equal(lister1, Encoding.UTF8.GetString(replayedLister1!.Bytes));
        Assert.Equal(lister2, Encoding.UTF8.GetString(replayedLister2!.Bytes));
        Assert.Contains("Yoga Tab Gen 2", Encoding.UTF8.GetString(replayedProduct!.Bytes), StringComparison.Ordinal);

        var latestLister = await corpus.QueryAsync(new FixtureQuery(SourceId: "lenovo-com", PageRole: "tablet-lister", Latest: true));
        Assert.Single(latestLister);
        Assert.Equal(listerRecord2.Id, latestLister[0].Id);
    }

    [Fact]
    public async Task PruneAsync_deletes_files_for_removed_records_and_reports_them()
    {
        var clock = new FakeTimeProvider(Epoch);
        var corpus = CreateCorpus(clock, fullRetentionCount: 1);
        var kept = new List<FixtureRecord>();
        for (var i = 0; i < 3; i++)
        {
            clock.Advance(TimeSpan.FromMinutes(1));
            kept.Add(await corpus.CaptureAsync(Capture("lenovo-com", $"<html><body>page {i}</body></html>", pageRole: $"page-{i}")));
        }

        var report = await corpus.PruneAsync(protectedFixtureIds: []);

        Assert.Equal(2, report.RemovedCount);
        foreach (var removed in report.Removed)
        {
            Assert.False(File.Exists(Path.Combine(_root, "fixtures", removed.File)));
        }
        var remaining = await corpus.QueryAsync(new FixtureQuery(SourceId: "lenovo-com"));
        Assert.Single(remaining);
        Assert.Equal(kept[^1].Id, remaining[0].Id);
    }
}
