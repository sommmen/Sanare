using Sanare.Http.Identity;

namespace Sanare.Http.Identity.Compliance;

/// <summary>
/// The compliance posture recorded for one source, reported to <c>IScraperAdministration</c>
/// (docs/features/browsing-identity.md, "Interfaces" &gt; "Outputs", AC-ID-015). Capability ids and the
/// proxy-provider id are modelled as opaque strings with no credential-shaped fields anywhere in this
/// record, so "without credentials" is a property of the type rather than of a redaction pass.
/// </summary>
/// <param name="SourceId">The source this report describes.</param>
/// <param name="Mode">The resolved acquisition mode.</param>
/// <param name="IdentityProfileId">The identity profile used for this source's requests.</param>
/// <param name="RobotsDecision">The robots.txt decision last recorded for this source (e.g. <c>"Allowed"</c>, <c>"Disallowed"</c>).</param>
/// <param name="RequestCount">The number of requests issued for this source since the report was last reset.</param>
/// <param name="EnabledCapabilityIds">Opaque identifiers for the stealth capabilities enabled for this source, if any.</param>
/// <param name="ProxyProviderId">An opaque identifier for the configured proxy provider, when stealth mode uses one.</param>
public sealed record ComplianceReport(
    string SourceId,
    AcquisitionMode Mode,
    string IdentityProfileId,
    string? RobotsDecision,
    long RequestCount,
    IReadOnlyList<string> EnabledCapabilityIds,
    string? ProxyProviderId);
