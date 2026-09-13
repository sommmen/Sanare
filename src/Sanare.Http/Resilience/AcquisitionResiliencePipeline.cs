using Sanare.Core.Acquisition;

namespace Sanare.Http.Resilience;

/// <summary>What the pipeline decided to do about one observed response or transport failure.</summary>
public enum RetryDisposition
{
    /// <summary>The response is final and successful enough to return to the caller.</summary>
    Accept,

    /// <summary>The failure is transient; try again after the computed delay.</summary>
    Retry,

    /// <summary>
    /// The host is refusing this client. Do not retry; the breaker accounts for it and the caller fails.
    /// </summary>
    Block,

    /// <summary>
    /// The resource is absent (<c>404</c>/<c>410</c>). Do not retry; return it intact so a plan's
    /// <c>notFound</c> predicate can classify it.
    /// </summary>
    NotFound,
}

/// <summary>One classification outcome, including how long to wait before retrying.</summary>
/// <param name="Disposition">What to do next.</param>
/// <param name="Delay">How long to wait before the retry, when <paramref name="Disposition"/> is <see cref="RetryDisposition.Retry"/>.</param>
/// <param name="Reason">A short operator-facing explanation.</param>
public readonly record struct RetryDecision(RetryDisposition Disposition, TimeSpan Delay, string Reason);

/// <summary>
/// Classifies acquisition outcomes into retry, block, not-found, or accept, and computes the backoff
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Retry and circuit breaking").
/// </summary>
/// <remarks>
/// <para>
/// Hand-rolled rather than layered on Polly or <c>Microsoft.Extensions.Http.Resilience</c> (DR-010): the
/// policy is a dozen lines of branching, and the assemblies under <c>src/</c> deliberately carry no
/// third-party resilience dependency.
/// </para>
/// <para>
/// Backoff is exponential with <em>full</em> jitter — the delay is drawn uniformly from
/// <c>[0, base · 2ⁿ]</c> rather than jittered around it. Full jitter is what actually de-synchronises a
/// fleet of clients that all got a <c>503</c> in the same second; jittering ±20 % around a shared
/// schedule leaves them very nearly in lockstep.
/// </para>
/// </remarks>
public sealed class AcquisitionResiliencePipeline
{
    private static readonly int[] RetryableStatusCodes = [408, 425, 429, 500, 502, 503, 504];

    private readonly RetryOptions _retry;
    private readonly TimeProvider _clock;
    private readonly Random _random;

    /// <summary>Creates a pipeline applying <paramref name="retry"/>.</summary>
    /// <param name="retry">The retry budget, base delay, and caps. Defaults are used when omitted.</param>
    /// <param name="clock">Resolves <c>Retry-After</c> HTTP-dates and paces waits. Inject a fake in tests.</param>
    /// <param name="random">Seeds the full-jitter draw so tests can assert an exact schedule.</param>
    public AcquisitionResiliencePipeline(RetryOptions? retry = null, TimeProvider? clock = null, Random? random = null)
    {
        _retry = retry ?? new RetryOptions();
        _clock = clock ?? TimeProvider.System;
        _random = random ?? Random.Shared;
    }

    /// <summary>The number of retries permitted beyond the first attempt.</summary>
    public int MaxAttempts => _retry.MaxAttempts;

    /// <summary>
    /// Classifies a response by status and headers.
    /// </summary>
    /// <param name="statusCode">The HTTP status observed.</param>
    /// <param name="attempt">The zero-based retry index already consumed.</param>
    /// <param name="retryAfterHeader">The raw <c>Retry-After</c> value, when present.</param>
    /// <param name="severity">The challenge classification of the response body, when one was computed.</param>
    /// <exception cref="AcquisitionException">
    /// <c>SNR-ACQ-002</c> when the host asked for a delay longer than the configured cap, which is failed
    /// immediately rather than slept off.
    /// </exception>
    public RetryDecision Classify(int statusCode, int attempt, string? retryAfterHeader = null, ChallengeSeverity severity = ChallengeSeverity.None)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);

        if (severity != ChallengeSeverity.None || statusCode == 403)
        {
            return new RetryDecision(RetryDisposition.Block, TimeSpan.Zero, $"HTTP {statusCode} carries a refusal signature; not retrying.");
        }

        if (statusCode is 404 or 410)
        {
            return new RetryDecision(RetryDisposition.NotFound, TimeSpan.Zero, $"HTTP {statusCode}; returned to the caller for notFound classification.");
        }

        if (!Array.Exists(RetryableStatusCodes, code => code == statusCode))
        {
            return new RetryDecision(RetryDisposition.Accept, TimeSpan.Zero, $"HTTP {statusCode} is final.");
        }

        if (attempt >= _retry.MaxAttempts)
        {
            return new RetryDecision(RetryDisposition.Accept, TimeSpan.Zero, $"HTTP {statusCode} after exhausting {_retry.MaxAttempts} retries.");
        }

        if (statusCode is 429 or 503)
        {
            var retryAfter = RetryAfterPolicy.Read(retryAfterHeader, _clock.GetUtcNow(), _retry.EffectiveRetryAfterCap);
            if (retryAfter.HasValue)
            {
                if (retryAfter.ExceedsCap)
                {
                    throw new AcquisitionException(
                        "SNR-ACQ-002",
                        $"The host asked for a {retryAfter.Delay.TotalSeconds:F0}s Retry-After delay, beyond the {_retry.EffectiveRetryAfterCap.TotalSeconds:F0}s cap.");
                }

                return new RetryDecision(RetryDisposition.Retry, retryAfter.Delay, $"HTTP {statusCode} with an honoured Retry-After.");
            }
        }

        return new RetryDecision(RetryDisposition.Retry, ComputeBackoff(attempt), $"HTTP {statusCode} is transient; retrying with backoff.");
    }

    /// <summary>
    /// Classifies a transport failure — a socket error or a timeout — which has no status code.
    /// </summary>
    /// <param name="attempt">The zero-based retry index already consumed.</param>
    public RetryDecision ClassifyTransportFailure(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(attempt);
        return attempt >= _retry.MaxAttempts
            ? new RetryDecision(RetryDisposition.Accept, TimeSpan.Zero, $"The transport failed after {_retry.MaxAttempts} retries.")
            : new RetryDecision(RetryDisposition.Retry, ComputeBackoff(attempt), "The transport failed transiently; retrying with backoff.");
    }

    /// <summary>Waits out <paramref name="decision"/>'s delay on the injected clock.</summary>
    /// <param name="decision">The decision whose delay to honour.</param>
    /// <param name="ct">Cancels the wait.</param>
    public async ValueTask WaitAsync(RetryDecision decision, CancellationToken ct = default)
    {
        if (decision.Delay > TimeSpan.Zero)
        {
            await Task.Delay(decision.Delay, _clock, ct).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var ceilingTicks = _retry.EffectiveBaseDelay.Ticks * (1L << Math.Min(attempt, 16));
        if (ceilingTicks > _retry.EffectiveMaxDelay.Ticks || ceilingTicks < 0)
        {
            ceilingTicks = _retry.EffectiveMaxDelay.Ticks;
        }

        return TimeSpan.FromTicks((long)(_random.NextDouble() * ceilingTicks));
    }
}
