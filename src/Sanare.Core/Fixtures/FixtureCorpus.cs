using System.Text;
using AngleSharp.Html.Parser;
using Sanare.Abstractions.Diagnostics;
using Sanare.Core.Fixtures.Hashing;
using Sanare.Core.Fixtures.Redaction;
using Sanare.Core.Fixtures.Retention;

namespace Sanare.Core.Fixtures;

public sealed class FixtureCorpus : IFixtureCorpus
{
    private readonly FixtureOptions _options;
    private readonly TimeProvider _clock;
    private readonly FixtureManifestStore _store;
    private readonly FixturePathBuilder _paths = new();
    private readonly IRedactor _redactor;
    private readonly ContentHasher _hasher;
    private readonly FixturePruner _pruner = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly Lazy<Task<List<FixtureRecord>>> _records;

    public FixtureCorpus(FixtureOptions options, TimeProvider? clock = null, IRedactor? redactor = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.FullRetentionCount < 0) { throw new ArgumentOutOfRangeException(nameof(options)); }
        _options = options;
        _clock = clock ?? TimeProvider.System;
        _store = new FixtureManifestStore(options.StateRoot);
        _redactor = redactor ?? new Redactor();
        _hasher = new ContentHasher(options.EffectiveVolatileJsonKeys);
        _records = new Lazy<Task<List<FixtureRecord>>>(LoadRecordsAsync);
    }

    public async ValueTask<FixtureRecord> CaptureAsync(CaptureRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.Offline) { throw new FixtureCorpusException("SNR-FIX-003", "Fixture capture is unavailable while offline."); }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Url);
        ArgumentNullException.ThrowIfNull(request.Body);
        var raw = await ReadBoundedAsync(request.Body, ct).ConfigureAwait(false);
        var redacted = _redactor.Redact(raw.Bytes, request.ResponseHeaders);
        var contentHash = _hasher.ContentHash(redacted.Content);
        var normalisedHash = _hasher.NormalisedHash(redacted.Content, request.ContentType);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = await _records.Value.ConfigureAwait(false);
            var match = records.FirstOrDefault(record => record.SourceId == request.SourceId && record.PageRole == request.PageRole && record.NormalisedHash == normalisedHash);
            if (match is not null)
            {
                var updated = match with { LastSeenAt = _clock.GetUtcNow() };
                records[records.IndexOf(match)] = updated;
                await SaveAsync(records, ct).ConfigureAwait(false);
                return updated;
            }

            var capturedAt = _clock.GetUtcNow();
            var relativeFile = _paths.BuildRelativeFile(request.SourceId, request.PageRole, capturedAt, contentHash, request.ContentType);
            var record = new FixtureRecord(
                _paths.BuildId(request.SourceId, request.PageRole, capturedAt, contentHash), request.SourceId, request.Url, capturedAt,
                request.Tier, request.ContentType, relativeFile, redacted.Content.LongLength, contentHash, normalisedHash,
                redacted.Rules, request.ReferencedByTags ?? [], request.PageRole, request.Notes, request.RetentionTier, request.PinnedIssue,
                capturedAt, raw.IsTruncated);
            var fullPath = Path.Combine(_options.StateRoot, "fixtures", relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.WriteAllBytesAsync(fullPath, redacted.Content, ct).ConfigureAwait(false);
            records.Add(record);
            await SaveAsync(records, ct).ConfigureAwait(false);
            return record;
        }
        finally { _writeLock.Release(); }
    }

    public async ValueTask<FixtureContent?> GetContentAsync(string fixtureId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureId);
        var record = (await _records.Value.ConfigureAwait(false)).FirstOrDefault(item => item.Id == fixtureId);
        if (record is null) { return null; }
        var path = Path.Combine(_options.StateRoot, "fixtures", record.File);
        if (!File.Exists(path)) { throw new FixtureCorpusException("SNR-FIX-002", $"Fixture file '{path}' is missing."); }
        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (_hasher.ContentHash(bytes) != record.ContentHash) { throw new FixtureCorpusException("SNR-FIX-002", $"Fixture file '{path}' is corrupt."); }
        return new FixtureContent(bytes, record.ContentType, record);
    }

    public async ValueTask<IReadOnlyList<FixtureRecord>> QueryAsync(FixtureQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        IEnumerable<FixtureRecord> result = await _records.Value.ConfigureAwait(false);
        if (query.FixtureId is not null) { result = result.Where(record => record.Id == query.FixtureId); }
        if (query.SourceId is not null) { result = result.Where(record => record.SourceId == query.SourceId); }
        if (query.Url is not null) { result = result.Where(record => record.Url == query.Url); }
        if (query.PageRole is not null) { result = result.Where(record => record.PageRole == query.PageRole); }
        result = result.OrderByDescending(record => record.CapturedAt);
        return query.Latest ? result.Take(1).ToArray() : result.ToArray();
    }

    public async ValueTask<FixtureSlice> SliceAsync(string fixtureId, FixtureSliceRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var content = await GetContentAsync(fixtureId, ct).ConfigureAwait(false) ?? throw new FixtureCorpusException("SNR-FIX-001", $"Fixture '{fixtureId}' was not found.");
        var text = Encoding.UTF8.GetString(content.Bytes);
        var requested = request.ContextCharacters < 0 ? 0 : request.ContextCharacters;
        var cap = Math.Min(requested, 32_000);
        var diagnostics = requested > 32_000
            ? new[] { new ScrapeDiagnostic("SNR-FIX-004", DiagnosticSeverity.Warning, "Fixture slice context was clamped to 32000 characters.") }
            : Array.Empty<ScrapeDiagnostic>();
        var target = request.Offset is { } offset ? (int)Math.Clamp(offset, 0, text.Length) : 0;
        string? selected = null;
        if (!string.IsNullOrWhiteSpace(request.Selector) && content.ContentType.Contains("html", StringComparison.OrdinalIgnoreCase))
        {
            var element = new HtmlParser().ParseDocument(text).QuerySelector(request.Selector);
            selected = element?.OuterHtml;
            if (selected is not null) { target = Math.Max(0, text.IndexOf(selected, StringComparison.Ordinal)); }
        }
        selected ??= string.Empty;
        var start = Math.Max(0, target - cap / 2);
        var end = Math.Min(text.Length, target + cap / 2);
        var context = text[start..end];
        var output = selected.Length == 0 || context.Contains(selected, StringComparison.Ordinal) ? context : selected + Environment.NewLine + context;
        return new FixtureSlice(fixtureId, output, Math.Max(0, text.Length - output.Length), diagnostics);
    }

    public async ValueTask<PruneReport> PruneAsync(IReadOnlyCollection<string> protectedFixtureIds, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(protectedFixtureIds);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var records = await _records.Value.ConfigureAwait(false);
            var removed = _pruner.SelectForRemoval(records, _options.FullRetentionCount, _options.EffectiveProtectedPageRoles, protectedFixtureIds);
            foreach (var record in removed)
            {
                var path = Path.Combine(_options.StateRoot, "fixtures", record.File);
                if (File.Exists(path)) { File.Delete(path); }
                records.Remove(record);
            }
            if (removed.Count > 0) { await SaveAsync(records, ct).ConfigureAwait(false); }
            return new PruneReport(removed);
        }
        finally { _writeLock.Release(); }
    }

    private async Task<List<FixtureRecord>> LoadRecordsAsync() => (await _store.LoadAsync().ConfigureAwait(false)).Fixtures.ToList();
    private ValueTask SaveAsync(List<FixtureRecord> records, CancellationToken ct) => _store.SaveAsync(new FixtureManifest(1, records), ct);

    private static async Task<(byte[] Bytes, bool IsTruncated)> ReadBoundedAsync(Stream stream, CancellationToken ct)
    {
        await using var output = new MemoryStream();
        var buffer = new byte[81920];
        var remaining = FixtureOptions.MaximumFixtureBytes;
        var truncated = false;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining + 1)), ct).ConfigureAwait(false);
            if (read == 0) { break; }
            if (read > remaining)
            {
                await output.WriteAsync(buffer.AsMemory(0, remaining), ct).ConfigureAwait(false);
                truncated = true;
                break;
            }
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            remaining -= read;
            if (remaining == 0)
            {
                if (await stream.ReadAsync(buffer.AsMemory(0, 1), ct).ConfigureAwait(false) > 0) { truncated = true; }
                break;
            }
        }
        return (output.ToArray(), truncated);
    }
}
