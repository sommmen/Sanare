using System.Net.Http.Headers;
using System.Text;
using Sanare.Core.Acquisition;
using Sanare.Http.Caching;

namespace Sanare.Http.Tests.Acquisition;

public sealed class CachePolicyTests
{
    [Fact]
    public void A_plain_200_is_storable()
    {
        Assert.True(new CachePolicy().CanStore(200, cacheControl: null));
    }

    [Theory]
    [InlineData(301)]
    [InlineData(404)]
    [InlineData(500)]
    public void Non_200_responses_are_not_stored(int statusCode)
    {
        Assert.False(new CachePolicy().CanStore(statusCode, cacheControl: null));
    }

    [Fact]
    public void No_store_is_honoured()
    {
        Assert.False(new CachePolicy().CanStore(200, new CacheControlHeaderValue { NoStore = true }));
    }

    [Fact]
    public void Private_responses_are_not_stored()
    {
        // A shared on-disk cache is by definition not a private one.
        Assert.False(new CachePolicy().CanStore(200, new CacheControlHeaderValue { Private = true }));
    }

    [Fact]
    public void Max_age_is_clamped_to_the_configured_floor()
    {
        var policy = new CachePolicy(new CacheOptions(MinFreshness: TimeSpan.FromMinutes(5)));
        var now = DateTimeOffset.UnixEpoch;

        var expiry = policy.ComputeExpiry(now, new CacheControlHeaderValue { MaxAge = TimeSpan.FromSeconds(1) }, expires: null);

        Assert.Equal(now + TimeSpan.FromMinutes(5), expiry);
    }

    [Fact]
    public void Max_age_is_clamped_to_the_configured_ceiling()
    {
        var policy = new CachePolicy(new CacheOptions(MaxFreshness: TimeSpan.FromDays(7)));
        var now = DateTimeOffset.UnixEpoch;

        var expiry = policy.ComputeExpiry(now, new CacheControlHeaderValue { MaxAge = TimeSpan.FromDays(365) }, expires: null);

        Assert.Equal(now + TimeSpan.FromDays(7), expiry);
    }

    [Fact]
    public void Set_cookie_never_reaches_the_cache_file()
    {
        // Persisting a session cookie to disk would turn a cache into a credential store.
        var sanitized = CachePolicy.Sanitize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Set-Cookie"] = "session=secret",
            ["Content-Type"] = "text/html",
        });

        Assert.False(sanitized.ContainsKey("Set-Cookie"));
        Assert.True(sanitized.ContainsKey("Content-Type"));
    }

    [Fact]
    public void Validators_are_read_back_from_headers()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ETag"] = "\"abc\"",
            ["Last-Modified"] = "Wed, 21 Oct 2015 07:28:00 GMT",
        };

        Assert.Equal("\"abc\"", CachePolicy.ReadETag(headers));
        Assert.NotNull(CachePolicy.ReadLastModified(headers));
    }

    [Fact]
    public void Conditional_headers_are_applied_from_a_stored_entry()
    {
        var entry = Entry(etag: "\"abc\"", expires: DateTimeOffset.UnixEpoch);
        using var message = new HttpRequestMessage(HttpMethod.Get, entry.Url);

        CachePolicy.ApplyConditionalHeaders(message, entry);

        Assert.Contains(message.Headers.IfNoneMatch, tag => tag.Tag == "\"abc\"");
    }

    internal static CachedResponse Entry(string? etag = null, DateTimeOffset? expires = null, string url = "https://example.test/a") =>
        new(
            Key: "key",
            Url: url,
            StatusCode: 200,
            ContentType: "text/html",
            Body: Encoding.UTF8.GetBytes("<html/>"),
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            StoredUtc: DateTimeOffset.UnixEpoch,
            ExpiresUtc: expires ?? DateTimeOffset.UnixEpoch.AddHours(1),
            ETag: etag,
            LastModified: null);
}

public sealed class FileHttpResponseCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"sanare-cache-{Guid.NewGuid():N}");

    [Fact]
    public async Task A_stored_entry_round_trips()
    {
        var cache = Create();
        var entry = CachePolicyTests.Entry();

        await cache.SetAsync(entry);
        var loaded = await cache.GetAsync(new Uri(entry.Url));

        Assert.NotNull(loaded);
        Assert.Equal(entry.Url, loaded.Url);
        Assert.Equal(entry.Body.ToArray(), loaded.Body.ToArray());
    }

    [Fact]
    public async Task A_miss_returns_null()
    {
        Assert.Null(await Create().GetAsync(new Uri("https://example.test/missing")));
    }

    [Fact]
    public async Task Distinct_urls_do_not_collide()
    {
        var cache = Create();
        await cache.SetAsync(CachePolicyTests.Entry(url: "https://example.test/a"));
        await cache.SetAsync(CachePolicyTests.Entry(url: "https://example.test/b", etag: "\"b\""));

        var a = await cache.GetAsync(new Uri("https://example.test/a"));
        var b = await cache.GetAsync(new Uri("https://example.test/b"));

        Assert.Null(a!.ETag);
        Assert.Equal("\"b\"", b!.ETag);
    }

    [Fact]
    public async Task Removal_is_idempotent()
    {
        var cache = Create();
        var url = new Uri("https://example.test/a");

        await cache.SetAsync(CachePolicyTests.Entry());
        await cache.RemoveAsync(url);
        await cache.RemoveAsync(url);

        Assert.Null(await cache.GetAsync(url));
    }

    [Fact]
    public async Task A_stale_entry_is_reported_as_such_rather_than_dropped()
    {
        // Staleness is not uselessness: a stale entry with a validator still saves a full body transfer.
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cache = Create();

        await cache.SetAsync(CachePolicyTests.Entry(etag: "\"abc\"", expires: DateTimeOffset.UnixEpoch.AddMinutes(5)));
        clock.Advance(TimeSpan.FromHours(1));

        var loaded = await cache.GetAsync(new Uri("https://example.test/a"));

        Assert.NotNull(loaded);
        Assert.False(loaded.IsFresh(clock.GetUtcNow()));
        Assert.True(loaded.CanRevalidate);
    }

    [Fact]
    public async Task A_corrupt_file_is_treated_as_a_miss()
    {
        // A cache that throws on damaged state converts a recoverable annoyance into a failed run.
        var cache = Create();
        await cache.SetAsync(CachePolicyTests.Entry());

        foreach (var file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
        {
            await File.WriteAllTextAsync(file, "{ not json");
        }

        Assert.Null(await cache.GetAsync(new Uri("https://example.test/a")));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a test over.
        }
    }

    private FileHttpResponseCache Create() => new(_root);
}
