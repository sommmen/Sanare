using Microsoft.Extensions.Logging;
using Sanare.Core.Acquisition;
using Sanare.Core.Fixtures;
using Sanare.Core.Observability;
using Sanare.Http.Caching;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Politeness;
using Sanare.Http.Robots;

namespace Sanare.Http;

/// <summary>
/// Assembles a fully governed <see cref="IContentAcquirer"/> from its parts
/// (docs/features/acquisition-pipeline.md, "File Structure").
/// </summary>
/// <remarks>
/// The library ships no DI container registration, so without this factory every consumer would have to
/// rediscover which of the eight collaborators are mandatory, which share state, and which must share a
/// <see cref="TimeProvider"/>. Getting that wrong is silent: a limiter registry built with a different
/// clock than the breaker still compiles and still runs, it just paces incorrectly.
/// </remarks>
public static class AcquisitionPipelineFactory
{
    /// <summary>
    /// Builds a governed acquirer over <paramref name="client"/>, sharing one clock and one limiter
    /// registry across every component that needs them.
    /// </summary>
    /// <param name="client">The HTTP client used for target traffic and robots fetches.</param>
    /// <param name="fixtures">The corpus every response is offered to.</param>
    /// <param name="options">The acquisition policy surface.</param>
    /// <param name="mode">The compliance posture robots decisions are evaluated under.</param>
    /// <param name="compliance">Receives robots decisions for the compliance report.</param>
    /// <param name="metrics">Receives request, delay, block, and cache counters.</param>
    /// <param name="clock">Shared by the limiters, breaker, cache expiry, and retry waits.</param>
    /// <param name="loggerFactory">Supplies the governed acquirer's logger.</param>
    public static IContentAcquirer Create(
        HttpClient client,
        IFixtureCorpus fixtures,
        AcquisitionOptions? options = null,
        AcquisitionMode mode = AcquisitionMode.Compliance,
        ComplianceReporter? compliance = null,
        ScraperMetrics? metrics = null,
        TimeProvider? clock = null,
        ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(fixtures);

        var resolved = options ?? new AcquisitionOptions();
        var resolvedClock = clock ?? TimeProvider.System;

        var limiters = new HostLimiterRegistry(resolved, resolvedClock, random: null, metrics);
        var robots = new RobotsPolicy(client, resolved, limiters, resolvedClock);
        var transport = new HttpContentAcquirer(client, fixtures, resolved, resolvedClock);
        var cache = resolved.EffectiveCache.Enabled
            ? new FileHttpResponseCache(resolved.EffectiveCache.Root, metrics)
            : null;

        return new GovernedContentAcquirer(
            transport,
            limiters,
            robots,
            resolved,
            mode,
            cache,
            compliance: compliance,
            metrics: metrics,
            clock: resolvedClock,
            logger: loggerFactory?.CreateLogger<GovernedContentAcquirer>());
    }
}
