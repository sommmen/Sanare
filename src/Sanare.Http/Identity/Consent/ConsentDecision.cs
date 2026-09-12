namespace Sanare.Http.Identity.Consent;

/// <summary>
/// The outcome of evaluating acquired content for a consent-wall signature
/// (docs/features/browsing-identity.md, "Interfaces" &gt; "Outputs").
/// </summary>
/// <param name="WallDetected">Whether a consent-wall signature was found.</param>
/// <param name="DetectedSignatureName">The name of the CMP signature that matched, when <see cref="WallDetected"/> is <see langword="true"/>.</param>
/// <param name="CookieName">The consent cookie to set, when a wall was detected.</param>
/// <param name="CookieValue">The consent cookie's value to set, when a wall was detected.</param>
/// <param name="RetryAttempted">
/// Whether this decision already represents the one permitted retry (docs/features/browsing-identity.md,
/// "Constraints" &gt; "Consent retry is capped at one"). A caller must not retry again once this is
/// <see langword="true"/> and the wall still shows as detected.
/// </param>
public sealed record ConsentDecision(
    bool WallDetected,
    string? DetectedSignatureName = null,
    string? CookieName = null,
    string? CookieValue = null,
    bool RetryAttempted = false)
{
    /// <summary>The shared "no consent wall found" result.</summary>
    public static readonly ConsentDecision NotDetected = new(WallDetected: false);
}
