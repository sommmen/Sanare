using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sanare.Core.Observability;

namespace Sanare.Http.Caching;

/// <summary>
/// A disk-backed HTTP response cache using two-level hash-prefixed directories
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Caching"; tech-design §10.3).
/// </summary>
/// <remarks>
/// <para>
/// Entries are filed under <c>{root}/ab/cd/{key}</c>, where <c>abcd…</c> is the SHA-256 of the absolute
/// URL. The two-level fan-out keeps any single directory small enough that filesystem enumeration stays
/// cheap once a corpus reaches tens of thousands of pages.
/// </para>
/// <para>
/// Every I/O failure is swallowed: a cache is an optimisation, and a full or read-only volume must
/// degrade into "no cache", never into a failed acquisition.
/// </para>
/// </remarks>
public sealed class FileHttpResponseCache : IHttpResponseCache
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _root;
    private readonly ScraperMetrics? _metrics;
    private long _hits;
    private long _misses;

    /// <summary>Creates a cache rooted at <paramref name="root"/>.</summary>
    /// <param name="root">The directory entries are written beneath.</param>
    /// <param name="metrics">Receives <c>sanare.cache.hit_ratio</c>, when supplied.</param>
    public FileHttpResponseCache(string root, ScraperMetrics? metrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        _root = root;
        _metrics = metrics;
    }

    /// <summary>The ratio of lookups served from disk, in <c>[0, 1]</c>.</summary>
    public double HitRatio
    {
        get
        {
            var hits = Interlocked.Read(ref _hits);
            var total = hits + Interlocked.Read(ref _misses);
            return total == 0 ? 0.0 : hits / (double)total;
        }
    }

    /// <summary>Computes the cache key for <paramref name="url"/>.</summary>
    /// <param name="url">The absolute URL to key.</param>
    public static string ComputeKey(Uri url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(url.AbsoluteUri)));
    }

    /// <inheritdoc />
    public async ValueTask<CachedResponse?> GetAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        var path = ResolvePath(ComputeKey(url));
        try
        {
            if (File.Exists(path))
            {
                await using var stream = File.OpenRead(path);
                var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, SerializerOptions, ct).ConfigureAwait(false);
                if (envelope is not null)
                {
                    Interlocked.Increment(ref _hits);
                    PublishRatio();
                    return envelope.ToResponse();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable entry is a miss, not a failure.
        }

        Interlocked.Increment(ref _misses);
        PublishRatio();
        return null;
    }

    /// <inheritdoc />
    public async ValueTask SetAsync(CachedResponse entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // The path is derived from the URL rather than from entry.Key so that a caller-supplied key can
        // never disagree with the one the readers compute, and can never escape the cache root.
        if (!Uri.TryCreate(entry.Url, UriKind.Absolute, out var url))
        {
            return;
        }

        var path = ResolvePath(ComputeKey(url));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Write to a sibling then move, so a crash mid-write cannot leave a truncated entry that a
            // later read would treat as authoritative.
            var temporary = path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, Envelope.From(entry), SerializerOptions, ct).ConfigureAwait(false);
            }

            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A cache write failure must never fail the acquisition that produced the content.
        }
    }

    /// <inheritdoc />
    public ValueTask RemoveAsync(Uri url, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(url);

        try
        {
            var path = ResolvePath(ComputeKey(url));
            if (File.Exists(path)) { File.Delete(path); }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing to do: the entry either never existed or will expire on its own.
        }

        return ValueTask.CompletedTask;
    }

    private void PublishRatio() => _metrics?.SetCacheHitRatio(HitRatio, "http");

    private string ResolvePath(string key) =>
        Path.Combine(_root, key[..2], key[2..4], key);

    private sealed record Envelope(
        string Key,
        string Url,
        int StatusCode,
        string ContentType,
        byte[] Body,
        Dictionary<string, string> Headers,
        DateTimeOffset StoredUtc,
        DateTimeOffset ExpiresUtc,
        string? ETag,
        DateTimeOffset? LastModified)
    {
        public static Envelope From(CachedResponse entry) => new(
            entry.Key,
            entry.Url,
            entry.StatusCode,
            entry.ContentType,
            entry.Body.ToArray(),
            new Dictionary<string, string>(entry.Headers, StringComparer.OrdinalIgnoreCase),
            entry.StoredUtc,
            entry.ExpiresUtc,
            entry.ETag,
            entry.LastModified);

        public CachedResponse ToResponse() => new(
            Key,
            Url,
            StatusCode,
            ContentType,
            Body,
            new Dictionary<string, string>(Headers, StringComparer.OrdinalIgnoreCase),
            StoredUtc,
            ExpiresUtc,
            ETag,
            LastModified);
    }
}
