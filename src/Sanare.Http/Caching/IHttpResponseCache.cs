namespace Sanare.Http.Caching;

/// <summary>A stored HTTP response body plus the headers needed to revalidate it.</summary>
/// <param name="Key">The cache key the entry is filed under.</param>
/// <param name="Url">The absolute URL the entry was fetched from.</param>
/// <param name="StatusCode">The status the origin returned.</param>
/// <param name="ContentType">The media type of <paramref name="Body"/>.</param>
/// <param name="Body">The stored response body.</param>
/// <param name="Headers">The stored header subset, with <c>Set-Cookie</c> already removed.</param>
/// <param name="StoredUtc">When the entry was written.</param>
/// <param name="ExpiresUtc">When the entry stops being servable without revalidation.</param>
/// <param name="ETag">The origin's <c>ETag</c>, when it supplied one.</param>
/// <param name="LastModified">The origin's <c>Last-Modified</c>, when it supplied one.</param>
public sealed record CachedResponse(
    string Key,
    string Url,
    int StatusCode,
    string ContentType,
    ReadOnlyMemory<byte> Body,
    IReadOnlyDictionary<string, string> Headers,
    DateTimeOffset StoredUtc,
    DateTimeOffset ExpiresUtc,
    string? ETag,
    DateTimeOffset? LastModified)
{
    /// <summary>Returns whether the entry may be served without contacting the origin.</summary>
    /// <param name="now">The current instant.</param>
    public bool IsFresh(DateTimeOffset now) => now < ExpiresUtc;

    /// <summary>Returns whether the entry carries a validator that makes a conditional request worthwhile.</summary>
    public bool CanRevalidate => ETag is not null || LastModified is not null;
}

/// <summary>
/// Stores HTTP responses so repeated acquisition of an unchanged page costs a conditional request rather
/// than a full transfer (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Caching").
/// </summary>
public interface IHttpResponseCache
{
    /// <summary>Returns the entry stored for <paramref name="url"/>, or <see langword="null"/>.</summary>
    /// <param name="url">The absolute URL to look up.</param>
    /// <param name="ct">Cancels the read.</param>
    ValueTask<CachedResponse?> GetAsync(Uri url, CancellationToken ct = default);

    /// <summary>Stores <paramref name="entry"/>, replacing any existing entry for the same URL.</summary>
    /// <param name="entry">The entry to write.</param>
    /// <param name="ct">Cancels the write.</param>
    ValueTask SetAsync(CachedResponse entry, CancellationToken ct = default);

    /// <summary>Removes the entry for <paramref name="url"/>, if any.</summary>
    /// <param name="url">The absolute URL to evict.</param>
    /// <param name="ct">Cancels the removal.</param>
    ValueTask RemoveAsync(Uri url, CancellationToken ct = default);
}
