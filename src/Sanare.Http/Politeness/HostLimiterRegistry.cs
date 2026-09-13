using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;

namespace Sanare.Http.Politeness;

/// <summary>
/// The process-wide per-host pacing budget: one token bucket and one concurrency semaphore per host,
/// plus the politeness gap between consecutive requests
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Rate limiting and politeness").
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="Shared"/> in production. Two concurrent runs against one host must contend for a
/// single budget — a per-run registry would silently double the request rate the operator configured,
/// which is the difference between polite and accidentally hostile (AC-027). The public constructor
/// exists so tests can build an isolated instance.
/// </para>
/// <para>
/// Waits are queued, not rejected: reaching a limit costs time, never a failure. Only cancellation or
/// the run's wall-clock ends a wait.
/// </para>
/// </remarks>
public sealed class HostLimiterRegistry : IHostLimiterRegistry, IAsyncDisposable
{
    private static readonly Lazy<HostLimiterRegistry> SharedInstance = new(() => new HostLimiterRegistry(new AcquisitionOptions()));

    private readonly ConcurrentDictionary<string, HostState> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly AcquisitionOptions _options;
    private readonly TimeProvider _clock;
    private readonly PolitenessDelay _delay;
    private readonly ScraperMetrics? _metrics;
    private int _disposed;

    /// <summary>Creates a registry that resolves budgets from <paramref name="options"/>.</summary>
    /// <param name="options">Supplies the root policy plus any host overrides.</param>
    /// <param name="clock">Drives politeness waits. Inject a fake in tests so no wall-clock time passes.</param>
    /// <param name="random">Seeds the jitter so tests can assert an exact schedule.</param>
    /// <param name="metrics">Receives <c>sanare.acquisition.delay</c>, when supplied.</param>
    public HostLimiterRegistry(
        AcquisitionOptions options,
        TimeProvider? clock = null,
        Random? random = null,
        ScraperMetrics? metrics = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? TimeProvider.System;
        _delay = new PolitenessDelay(random);
        _metrics = metrics;
    }

    /// <summary>
    /// The process-wide registry backing production acquisition, configured from defaults. Prefer
    /// constructing a registry explicitly wherever the host application owns composition.
    /// </summary>
    public static HostLimiterRegistry Shared => SharedInstance.Value;

    /// <inheritdoc />
    public HostBudget GetBudget(string host) => GetState(host).Budget;

    /// <inheritdoc />
    public void ApplyCrawlDelay(string host, TimeSpan? crawlDelay)
    {
        var state = GetState(host);
        lock (state.Gate)
        {
            state.Budget = state.Budget.WithCrawlDelay(crawlDelay);
        }
    }

    /// <inheritdoc />
    public async ValueTask<IAsyncDisposable> AcquireAsync(string host, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var state = GetState(host);

        // Concurrency first, then a token. Taking the slot first means a request that is about to wait
        // for a token is already counted against the host's in-flight ceiling, so the ceiling bounds
        // waiting requests too rather than only ones on the wire.
        await state.Concurrency.WaitAsync(ct).ConfigureAwait(false);
        RateLimitLease? lease = null;
        try
        {
            lease = await state.Tokens.AcquireAsync(permitCount: 1, ct).ConfigureAwait(false);
            if (!lease.IsAcquired)
            {
                lease.Dispose();
                lease = null;
                throw new AcquisitionException("SNR-ACQ-002", $"The rate limiter refused a permit for '{host}'.");
            }
        }
        catch
        {
            lease?.Dispose();
            state.Concurrency.Release();
            throw;
        }

        return new HostLease(state, lease);
    }

    /// <inheritdoc />
    public async ValueTask WaitForPolitenessAsync(string host, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var state = GetState(host);
        TimeSpan wait;
        lock (state.Gate)
        {
            var target = _delay.Compute(state.Budget);
            var now = _clock.GetUtcNow();
            wait = state.LastRequestUtc is { } last ? target - (now - last) : TimeSpan.Zero;
            if (wait < TimeSpan.Zero) { wait = TimeSpan.Zero; }

            // Stamp the *scheduled* instant, not the current one, so concurrent callers space out
            // against each other instead of all measuring from the same stale point.
            state.LastRequestUtc = now + wait;
        }

        if (wait > TimeSpan.Zero)
        {
            _metrics?.RecordDelay(wait.TotalMilliseconds, host, "politeness");
            await Task.Delay(wait, _clock, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }

        foreach (var state in _hosts.Values)
        {
            await state.Tokens.DisposeAsync().ConfigureAwait(false);
            state.Concurrency.Dispose();
        }

        _hosts.Clear();
    }

    private HostState GetState(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        return _hosts.GetOrAdd(host, static (key, self) => self.CreateState(key), this);
    }

    private HostState CreateState(string host)
    {
        var rateLimit = _options.ResolveFor(host).RateLimit;

        // Replenish once per second rather than once per minute so a host that has been idle does not
        // hand out a full minute's worth of permits the instant the next request arrives.
        var tokens = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = rateLimit.Burst,
            TokensPerPeriod = Math.Max(1, (int)Math.Round(rateLimit.RequestsPerMinute / 60.0, MidpointRounding.AwayFromZero)),
            ReplenishmentPeriod = TimeSpan.FromSeconds(Math.Max(1.0, 60.0 / rateLimit.RequestsPerMinute)),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });

        return new HostState(new HostBudget(host, rateLimit), tokens, new SemaphoreSlim(rateLimit.MaxConcurrencyPerHost, rateLimit.MaxConcurrencyPerHost));
    }

    private sealed class HostState(HostBudget budget, TokenBucketRateLimiter tokens, SemaphoreSlim concurrency)
    {
        public Lock Gate { get; } = new();

        public HostBudget Budget { get; set; } = budget;

        public TokenBucketRateLimiter Tokens { get; } = tokens;

        public SemaphoreSlim Concurrency { get; } = concurrency;

        public DateTimeOffset? LastRequestUtc { get; set; }
    }

    private sealed class HostLease(HostState state, RateLimitLease lease) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                lease.Dispose();
                state.Concurrency.Release();
            }

            return ValueTask.CompletedTask;
        }
    }
}
