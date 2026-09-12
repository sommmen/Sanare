using System.Collections.Concurrent;

namespace Sanare.Http.Identity.Compliance;

/// <summary>
/// Accumulates and reports the per-source compliance posture (docs/features/browsing-identity.md,
/// "Key Behaviors" &gt; "Stability" — the profile id is written into the compliance report so a site
/// operator's question, "what was this?", has a precise answer). In-memory only; there is no
/// persistence surface.
/// </summary>
public sealed class ComplianceReporter
{
    private sealed class MutableState
    {
        public AcquisitionMode Mode { get; set; }
        public string IdentityProfileId { get; set; } = string.Empty;
        public string? RobotsDecision { get; set; }
        public long RequestCount { get; set; }
        public IReadOnlyList<string> EnabledCapabilityIds { get; set; } = [];
        public string? ProxyProviderId { get; set; }
    }

    private readonly ConcurrentDictionary<string, MutableState> _stateBySource = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the resolved mode and identity profile for a source. Call once per source at
    /// configuration time, or whenever the resolution changes.
    /// </summary>
    public void RecordResolution(string sourceId, AcquisitionMode mode, string identityProfileId, IReadOnlyList<string>? enabledCapabilityIds = null, string? proxyProviderId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityProfileId);
        var state = _stateBySource.GetOrAdd(sourceId, static _ => new MutableState());
        lock (state)
        {
            state.Mode = mode;
            state.IdentityProfileId = identityProfileId;
            state.EnabledCapabilityIds = enabledCapabilityIds ?? [];
            state.ProxyProviderId = proxyProviderId;
        }
    }

    /// <summary>Records the most recent robots.txt decision for a source.</summary>
    public void RecordRobotsDecision(string sourceId, string robotsDecision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(robotsDecision);
        var state = _stateBySource.GetOrAdd(sourceId, static _ => new MutableState());
        lock (state)
        {
            state.RobotsDecision = robotsDecision;
        }
    }

    /// <summary>Increments the request count recorded for a source.</summary>
    public void RecordRequest(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        var state = _stateBySource.GetOrAdd(sourceId, static _ => new MutableState());
        lock (state)
        {
            state.RequestCount++;
        }
    }

    /// <summary>Returns the current compliance report for a source, or an empty report if nothing has been recorded yet.</summary>
    public ComplianceReport GetReport(string sourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        if (!_stateBySource.TryGetValue(sourceId, out var state))
        {
            return new ComplianceReport(sourceId, AcquisitionMode.Compliance, string.Empty, null, 0, [], null);
        }

        lock (state)
        {
            return new ComplianceReport(
                sourceId,
                state.Mode,
                state.IdentityProfileId,
                state.RobotsDecision,
                state.RequestCount,
                state.EnabledCapabilityIds,
                state.ProxyProviderId);
        }
    }
}
