using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;

namespace Sanare.Http.Identity.Consent;

/// <summary>
/// Detects consent walls in acquired content and decides the standard consent-cookie response
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Consent walls").
/// </summary>
public interface IConsentPolicy
{
    /// <summary>
    /// Evaluates the given content for a consent-wall signature. Detection requires both a matching
    /// CMP marker and the absence of the expected content root — the second half is what stops a
    /// false positive on a page that merely ships a CMP script but has already rendered content.
    /// </summary>
    /// <param name="content">The acquired content to evaluate.</param>
    /// <param name="expectedContentRootSelector">
    /// A CSS selector for content that is expected to be present when the page is not walled (e.g.
    /// the extraction plan's root selector). Its absence, together with a CMP marker, is what
    /// constitutes detection.
    /// </param>
    /// <param name="consentSpec">An optional per-source override selector/strategy (from the plan's <see cref="ConsentSpec"/>).</param>
    /// <param name="retryAttempted">Whether the single permitted retry has already happened for this URL.</param>
    ConsentDecision Evaluate(AcquiredContent content, string expectedContentRootSelector, ConsentSpec? consentSpec, bool retryAttempted);
}
