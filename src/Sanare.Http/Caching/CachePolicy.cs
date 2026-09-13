using System.Globalization;
using System.Net.Http.Headers;
using Sanare.Core.Acquisition;

namespace Sanare.Http.Caching;

/// <summary>
/// Decides what may be cached and for how long, from the origin's <c>Cache-Control</c> and <c>Expires</c>
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Caching").
/// </summary>
/// <remarks>
/// Freshness is clamped to <c>[MinFreshness, MaxFreshness]</c> — 5 minutes to 7 days by default. The floor
/// stops a host that sends <c>max-age=0</c> on every page from defeating the cache entirely during a
/// single run; the ceiling stops a host that sends <c>max-age=31536000</c> from pinning stale content for
/// a year. Both bounds exist because the origin's caching intent is aimed at browsers, not at a scraper
/// whose correctness depends on noticing that a page changed.
/// </remarks>
public sealed class CachePolicy(CacheOptions? options = null)
{
    private static readonly string[] StrippedHeaders =
    [
        "Set-Cookie",
        "Set-Cookie2",
        "Authorization",
        "Proxy-Authenticate",
        "WWW-Authenticate",
    ];

    private readonly CacheOptions _options = options ?? new CacheOptions();

    /// <summary>Whether caching is enabled at all.</summary>
    public bool Enabled => _options.Enabled;

    /// <summary>
    /// Returns whether a response may be written to the cache. A <c>no-store</c> directive always wins;
    /// fixture capture is a separate, explicitly-recorded mechanism and proceeds regardless.
    /// </summary>
    /// <param name="statusCode">The status the origin returned.</param>
    /// <param name="cacheControl">The origin's <c>Cache-Control</c>, when present.</param>
    public bool CanStore(int statusCode, CacheControlHeaderValue? cacheControl)
    {
        if (!_options.Enabled || statusCode is < 200 or >= 300)
        {
            return false;
        }

        return cacheControl is null || (!cacheControl.NoStore && !cacheControl.Private);
    }

    /// <summary>
    /// Computes when a response stops being servable without revalidation, clamped to the configured
    /// freshness window.
    /// </summary>
    /// <param name="now">The current instant.</param>
    /// <param name="cacheControl">The origin's <c>Cache-Control</c>, when present.</param>
    /// <param name="expires">The origin's <c>Expires</c>, when present.</param>
    public DateTimeOffset ComputeExpiry(DateTimeOffset now, CacheControlHeaderValue? cacheControl, DateTimeOffset? expires)
    {
        TimeSpan lifetime;
        if (cacheControl?.MaxAge is { } maxAge)
        {
            lifetime = maxAge;
        }
        else if (expires is { } absolute)
        {
            lifetime = absolute - now;
        }
        else
        {
            lifetime = _options.EffectiveMinFreshness;
        }

        if (lifetime < _options.EffectiveMinFreshness) { lifetime = _options.EffectiveMinFreshness; }
        if (lifetime > _options.EffectiveMaxFreshness) { lifetime = _options.EffectiveMaxFreshness; }
        return now + lifetime;
    }

    /// <summary>
    /// Adds the conditional headers that let the origin answer <c>304</c> instead of resending the body.
    /// </summary>
    /// <param name="message">The outbound request to decorate.</param>
    /// <param name="entry">The cached entry supplying the validators.</param>
    public static void ApplyConditionalHeaders(HttpRequestMessage message, CachedResponse entry)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.ETag is { } etag && EntityTagHeaderValue.TryParse(etag, out var parsed))
        {
            message.Headers.IfNoneMatch.Add(parsed);
        }

        if (entry.LastModified is { } lastModified)
        {
            message.Headers.IfModifiedSince = lastModified;
        }
    }

    /// <summary>
    /// Returns the subset of <paramref name="headers"/> safe to persist, with credential-bearing headers
    /// removed. Cached entries live on disk and outlive the run; a stored <c>Set-Cookie</c> would be a
    /// credential leak with a very long tail.
    /// </summary>
    /// <param name="headers">The response headers to filter.</param>
    public static IReadOnlyDictionary<string, string> Sanitize(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in headers)
        {
            if (!Array.Exists(StrippedHeaders, stripped => string.Equals(stripped, name, StringComparison.OrdinalIgnoreCase)))
            {
                result[name] = value;
            }
        }

        return result;
    }

    /// <summary>Reads an <c>ETag</c> out of a sanitized header dictionary.</summary>
    /// <param name="headers">The headers to read.</param>
    public static string? ReadETag(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers.TryGetValue("ETag", out var etag) && !string.IsNullOrWhiteSpace(etag) ? etag : null;
    }

    /// <summary>Reads a <c>Last-Modified</c> out of a sanitized header dictionary.</summary>
    /// <param name="headers">The headers to read.</param>
    public static DateTimeOffset? ReadLastModified(IReadOnlyDictionary<string, string> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return headers.TryGetValue("Last-Modified", out var raw)
            && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }
}
