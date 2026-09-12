using System.Collections.Concurrent;
using System.Net;

namespace Sanare.Http.Identity.Consent;

/// <summary>
/// A per-host, in-memory, bounded, inspectable cookie store keyed by registrable host. Expiry is
/// honoured against an injected <see cref="TimeProvider"/> (matching <c>HttpContentAcquirer</c>'s
/// clock convention) rather than wall-clock time, so it can be tested deterministically. Exposes no
/// serialization surface — there is no save/load path — which is what mechanically keeps cookies out
/// of fixtures, logs, and telemetry (docs/features/browsing-identity.md, "Constraints").
/// </summary>
/// <param name="clock">The clock used to evaluate cookie expiry. Defaults to <see cref="TimeProvider.System"/>.</param>
/// <param name="maxCookiesPerHost">The maximum number of cookies retained per host, to bound memory use.</param>
public sealed class HostCookieJar(TimeProvider? clock = null, int maxCookiesPerHost = 32)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Cookie>> _cookiesByHost = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxCookiesPerHost = maxCookiesPerHost;

    /// <summary>Sets (or replaces) a cookie for the given registrable host.</summary>
    public void Set(string host, Cookie cookie)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentNullException.ThrowIfNull(cookie);
        var hostCookies = _cookiesByHost.GetOrAdd(host, static _ => new ConcurrentDictionary<string, Cookie>(StringComparer.Ordinal));
        hostCookies[cookie.Name] = cookie;

        // Bound memory use: drop the oldest entries (by nothing meaningful to order by other than
        // insertion — ConcurrentDictionary does not preserve it — so this is a coarse, best-effort cap).
        if (hostCookies.Count > _maxCookiesPerHost)
        {
            foreach (var key in hostCookies.Keys.Take(hostCookies.Count - _maxCookiesPerHost))
            {
                hostCookies.TryRemove(key, out _);
            }
        }
    }

    /// <summary>Returns the non-expired cookies currently held for the given registrable host.</summary>
    public IReadOnlyList<Cookie> Get(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!_cookiesByHost.TryGetValue(host, out var hostCookies))
        {
            return [];
        }

        var now = _clock.GetUtcNow();
        List<Cookie> live = [];
        foreach (var cookie in hostCookies.Values)
        {
            if (IsExpired(cookie, now))
            {
                hostCookies.TryRemove(cookie.Name, out _);
                continue;
            }

            live.Add(cookie);
        }

        return live;
    }

    /// <summary>Removes every cookie held for the given registrable host.</summary>
    public void Clear(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        _cookiesByHost.TryRemove(host, out _);
    }

    private static bool IsExpired(Cookie cookie, DateTimeOffset now) =>
        cookie.Expires != DateTime.MinValue && cookie.Expires.ToUniversalTime() <= now.UtcDateTime;
}
