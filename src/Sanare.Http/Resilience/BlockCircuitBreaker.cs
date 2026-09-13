using System.Collections.Concurrent;
using Sanare.Core.Acquisition;
using Sanare.Core.Observability;

namespace Sanare.Http.Resilience;

/// <summary>Why a host's circuit is open.</summary>
public enum BreakerState
{
    /// <summary>Requests flow normally.</summary>
    Closed,

    /// <summary>
    /// A generic <c>403</c>/challenge streak tripped the breaker. Time-boxed: it auto-closes after the
    /// configured cool-down and the run reports <c>Blocked</c>.
    /// </summary>
    Blocked,

    /// <summary>
    /// A hard challenge or IP-block signature tripped the breaker. Not time-boxed: it stays open until a
    /// widening re-probe succeeds cleanly or an operator hands off, and the run reports
    /// <c>ChallengePaused</c>.
    /// </summary>
    ChallengePaused,
}

/// <summary>
/// Per-host block accounting with two distinct open states
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Retry and circuit breaking").
/// </summary>
/// <remarks>
/// <para>
/// The two states exist because they mean different things. A generic <c>403</c> streak is usually
/// transient — a rate trip, a stale session — so a 30-minute cool-down and automatic resumption is right.
/// A Cloudflare interstitial or an IP-block page is a deliberate, durable decision by the host; retrying
/// it on a timer is both futile and rude, so that state never auto-closes on a fixed schedule.
/// </para>
/// <para>
/// The paused state still re-probes, at a widening interval that never runs faster than the baseline
/// cool-down. That is a slow, honest check for whether the block has lifted, not an attempt to outlast
/// it (DR-006, NG-1–NG-3).
/// </para>
/// </remarks>
public sealed class BlockCircuitBreaker
{
    private readonly ConcurrentDictionary<string, HostCircuit> _hosts = new(StringComparer.OrdinalIgnoreCase);
    private readonly AcquisitionOptions _options;
    private readonly TimeProvider _clock;
    private readonly ScraperMetrics? _metrics;

    /// <summary>Creates a breaker reading thresholds from <paramref name="options"/>.</summary>
    /// <param name="options">Supplies the streak thresholds, window, and cool-down.</param>
    /// <param name="clock">Drives windows and cool-downs. Inject a fake in tests.</param>
    /// <param name="metrics">Receives the blocked and challenge-paused counters, when supplied.</param>
    public BlockCircuitBreaker(AcquisitionOptions? options = null, TimeProvider? clock = null, ScraperMetrics? metrics = null)
    {
        _options = options ?? new AcquisitionOptions();
        _clock = clock ?? TimeProvider.System;
        _metrics = metrics;
    }

    /// <summary>Returns the current state of <paramref name="host"/>'s circuit.</summary>
    /// <param name="host">The host to inspect.</param>
    public BreakerState GetState(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!_hosts.TryGetValue(host, out var circuit))
        {
            return BreakerState.Closed;
        }

        lock (circuit.Gate)
        {
            RefreshLocked(circuit);
            return circuit.State;
        }
    }

    /// <summary>
    /// Fails fast when <paramref name="host"/>'s circuit is open, without touching the network. Returns
    /// quietly when the circuit is closed or a re-probe is due.
    /// </summary>
    /// <param name="host">The host about to be requested.</param>
    /// <exception cref="AcquisitionException">
    /// <c>SNR-ACQ-003</c> when the circuit is time-boxed open, <c>SNR-ACQ-011</c> when the source is paused
    /// on a hard challenge.
    /// </exception>
    public void ThrowIfOpen(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!_hosts.TryGetValue(host, out var circuit))
        {
            return;
        }

        lock (circuit.Gate)
        {
            RefreshLocked(circuit);
            switch (circuit.State)
            {
                case BreakerState.Blocked:
                    throw new AcquisitionException("SNR-ACQ-003", $"The circuit for '{host}' is open after repeated blocks; not opening a socket.");
                case BreakerState.ChallengePaused:
                    throw new AcquisitionException("SNR-ACQ-011", $"Acquisition from '{host}' is paused on a challenge signature; operator action is required.");
                case BreakerState.Closed:
                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Records a clean response, which clears the streak and closes a paused circuit whose re-probe just
    /// succeeded.
    /// </summary>
    /// <param name="host">The host that responded cleanly.</param>
    public void RecordSuccess(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (!_hosts.TryGetValue(host, out var circuit))
        {
            return;
        }

        lock (circuit.Gate)
        {
            circuit.Strikes.Clear();
            circuit.State = BreakerState.Closed;
            circuit.OpenedUtc = null;
            circuit.ProbeDueUtc = null;
            circuit.ProbeMultiplier = 1;
        }
    }

    /// <summary>
    /// Records a block signal. A <see cref="ChallengeSeverity.Hard"/> signature pauses the host
    /// immediately; a generic one counts toward the streak and opens the time-boxed circuit once the
    /// threshold is reached within the window.
    /// </summary>
    /// <param name="host">The host that pushed back.</param>
    /// <param name="severity">How hard the refusal was.</param>
    public BreakerState RecordBlock(string host, ChallengeSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        if (severity == ChallengeSeverity.None)
        {
            return GetState(host);
        }

        var circuit = _hosts.GetOrAdd(host, static _ => new HostCircuit());
        var breaker = _options.EffectiveBreaker;
        var now = _clock.GetUtcNow();

        lock (circuit.Gate)
        {
            if (severity == ChallengeSeverity.Hard)
            {
                circuit.State = BreakerState.ChallengePaused;
                circuit.OpenedUtc = now;
                circuit.ProbeMultiplier = 1;
                circuit.ProbeDueUtc = now + breaker.EffectiveOpenDuration;
                _metrics?.RecordChallengePaused(host);
                return circuit.State;
            }

            var windowStart = now - breaker.EffectiveBlockWindow;
            circuit.Strikes.RemoveAll(strike => strike < windowStart);
            circuit.Strikes.Add(now);

            if (circuit.Strikes.Count >= breaker.BlockThreshold)
            {
                circuit.State = BreakerState.Blocked;
                circuit.OpenedUtc = now;
                circuit.ProbeDueUtc = now + breaker.EffectiveOpenDuration;
                _metrics?.RecordBlocked(host, "streak");
            }

            return circuit.State;
        }
    }

    private void RefreshLocked(HostCircuit circuit)
    {
        if (circuit.State == BreakerState.Closed || circuit.ProbeDueUtc is not { } due)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        if (now < due)
        {
            return;
        }

        if (circuit.State == BreakerState.Blocked)
        {
            // Time-boxed: the cool-down elapsed, so resume normally.
            circuit.State = BreakerState.Closed;
            circuit.Strikes.Clear();
            circuit.OpenedUtc = null;
            circuit.ProbeDueUtc = null;
            circuit.ProbeMultiplier = 1;
            return;
        }

        // Paused: allow exactly one probe through, and widen the next interval so a host that keeps
        // refusing is asked less and less often. RecordSuccess closes the circuit if the probe is clean.
        var breaker = _options.EffectiveBreaker;
        circuit.ProbeMultiplier = Math.Min(circuit.ProbeMultiplier * 2, 48);
        circuit.ProbeDueUtc = now + (breaker.EffectiveOpenDuration * circuit.ProbeMultiplier);
        circuit.State = BreakerState.Closed;
    }

    /// <summary>
    /// Closes <paramref name="host"/>'s circuit unconditionally. Reserved for the supervised operator
    /// hand-off; the pipeline itself never calls this.
    /// </summary>
    /// <param name="host">The host to resume.</param>
    internal void ForceClose(string host) => RecordSuccess(host);

    private sealed class HostCircuit
    {
        public Lock Gate { get; } = new();

        public List<DateTimeOffset> Strikes { get; } = [];

        public BreakerState State { get; set; } = BreakerState.Closed;

        public DateTimeOffset? OpenedUtc { get; set; }

        public DateTimeOffset? ProbeDueUtc { get; set; }

        public int ProbeMultiplier { get; set; } = 1;
    }
}
