namespace Sanare.Http.Politeness;

/// <summary>
/// Hands out the per-host pacing leases every outbound target request must hold
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Rate limiting and politeness").
/// Implementations are process-wide so two concurrent runs against one host share a single budget
/// rather than doubling the load the operator configured (AC-027).
/// </summary>
public interface IHostLimiterRegistry
{
    /// <summary>
    /// Waits until <paramref name="host"/> has both a token and a free concurrency slot, then returns a
    /// lease that releases both when disposed. Callers queue rather than fail: only cancellation ends the
    /// wait.
    /// </summary>
    /// <param name="host">The host to acquire against, matched case-insensitively.</param>
    /// <param name="ct">Cancels the wait, releasing anything already taken.</param>
    ValueTask<IAsyncDisposable> AcquireAsync(string host, CancellationToken ct = default);

    /// <summary>
    /// Returns the budget in force for <paramref name="host"/>, creating it from configuration on first
    /// use.
    /// </summary>
    /// <param name="host">The host to look up, matched case-insensitively.</param>
    HostBudget GetBudget(string host);

    /// <summary>
    /// Records the <c>Crawl-delay</c> a host advertises so the politeness floor can honour it. Supplying a
    /// delay shorter than the configured one has no effect — the larger value always wins.
    /// </summary>
    /// <param name="host">The host the delay was advertised by.</param>
    /// <param name="crawlDelay">The advertised delay, or <see langword="null"/> to clear it.</param>
    void ApplyCrawlDelay(string host, TimeSpan? crawlDelay);

    /// <summary>
    /// Waits out the politeness gap still owed to <paramref name="host"/> since its last request, then
    /// stamps the current instant as that host's most recent request.
    /// </summary>
    /// <param name="host">The host being paced.</param>
    /// <param name="ct">Cancels the wait.</param>
    ValueTask WaitForPolitenessAsync(string host, CancellationToken ct = default);
}
