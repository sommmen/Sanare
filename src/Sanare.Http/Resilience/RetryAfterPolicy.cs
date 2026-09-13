using System.Globalization;
using System.Net.Http.Headers;

namespace Sanare.Http.Resilience;

/// <summary>
/// Interprets the <c>Retry-After</c> header a host sends with a <c>429</c> or <c>503</c>
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Retry and circuit breaking", row 2).
/// </summary>
/// <remarks>
/// Both wire forms are honoured: delta-seconds and an HTTP-date. A delay beyond the configured cap is
/// <em>not</em> slept off — the request fails <c>SNR-ACQ-002</c> immediately. Holding a worker for ten
/// minutes because a host asked politely would blow the run's wall-clock budget while looking like a hang.
/// </remarks>
public static class RetryAfterPolicy
{
    /// <summary>The outcome of reading a <c>Retry-After</c> header.</summary>
    /// <param name="HasValue">Whether the header was present and parseable.</param>
    /// <param name="Delay">The indicated delay, when <paramref name="HasValue"/> is set.</param>
    /// <param name="ExceedsCap">Whether the indicated delay is longer than the configured cap.</param>
    public readonly record struct RetryAfterResult(bool HasValue, TimeSpan Delay, bool ExceedsCap);

    /// <summary>
    /// Reads <paramref name="headers"/>' <c>Retry-After</c> relative to <paramref name="now"/> and
    /// compares it to <paramref name="cap"/>.
    /// </summary>
    /// <param name="headers">The response headers to read.</param>
    /// <param name="now">The current instant, used to convert an HTTP-date into a delay.</param>
    /// <param name="cap">The longest delay worth waiting out.</param>
    public static RetryAfterResult Read(HttpResponseHeaders? headers, DateTimeOffset now, TimeSpan cap)
    {
        var value = headers?.RetryAfter;
        if (value is null)
        {
            return new RetryAfterResult(HasValue: false, TimeSpan.Zero, ExceedsCap: false);
        }

        TimeSpan delay;
        if (value.Delta is { } delta)
        {
            delay = delta;
        }
        else if (value.Date is { } date)
        {
            delay = date - now;
        }
        else
        {
            return new RetryAfterResult(HasValue: false, TimeSpan.Zero, ExceedsCap: false);
        }

        if (delay < TimeSpan.Zero)
        {
            // A date already in the past means "retry now".
            delay = TimeSpan.Zero;
        }

        return new RetryAfterResult(HasValue: true, delay, delay > cap);
    }

    /// <summary>
    /// Reads a raw header value, for callers holding strings rather than a typed header collection.
    /// </summary>
    /// <param name="headerValue">The raw <c>Retry-After</c> value.</param>
    /// <param name="now">The current instant.</param>
    /// <param name="cap">The longest delay worth waiting out.</param>
    public static RetryAfterResult Read(string? headerValue, DateTimeOffset now, TimeSpan cap)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return new RetryAfterResult(HasValue: false, TimeSpan.Zero, ExceedsCap: false);
        }

        var trimmed = headerValue.Trim();
        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
        {
            var delay = seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
            return new RetryAfterResult(HasValue: true, delay, delay > cap);
        }

        if (DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var date))
        {
            var delay = date - now;
            if (delay < TimeSpan.Zero) { delay = TimeSpan.Zero; }
            return new RetryAfterResult(HasValue: true, delay, delay > cap);
        }

        return new RetryAfterResult(HasValue: false, TimeSpan.Zero, ExceedsCap: false);
    }
}
