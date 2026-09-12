using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Identity.Consent;

namespace Sanare.Http.Identity;

/// <summary>
/// Composes coherent request identities, detects and clears consent walls, and reports compliance
/// posture per source (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Interface").
/// </summary>
public interface IBrowsingIdentityProvider
{
    /// <summary>Composes a coherent identity for the given request.</summary>
    BrowsingIdentity GetIdentity(IdentityRequest request);

    /// <summary>
    /// Evaluates acquired content for a consent-wall signature and, when found, the standard
    /// consent-cookie response to persist and retry with.
    /// </summary>
    ConsentDecision EvaluateConsent(AcquiredContent content, string sourceId);

    /// <summary>Returns the current compliance posture recorded for the given source.</summary>
    ComplianceReport GetComplianceReport(string sourceId);
}
