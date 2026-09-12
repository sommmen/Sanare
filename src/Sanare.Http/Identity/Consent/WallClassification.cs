namespace Sanare.Http.Identity.Consent;

/// <summary>
/// The terminal classification of a non-consent wall encountered during acquisition
/// (docs/features/browsing-identity.md, "Acceptance Criteria" AC-ID-016, AC-ID-017). Both member
/// classifications are pure, detector-produced facts with no associated remediation path — there is
/// no solver seam anywhere in this assembly for <see cref="Challenge"/>, and no bypass path for
/// <see cref="UnavailablePublicContent"/>.
/// </summary>
public enum WallClassification
{
    /// <summary>No wall signature was detected.</summary>
    None,

    /// <summary>
    /// A CAPTCHA signature was detected (AC-ID-016). Solving through a service, human-in-the-loop
    /// completion, and browser-agent solving are future work and out of scope here.
    /// </summary>
    Challenge,

    /// <summary>
    /// A login or paywall signature was detected (AC-ID-017). Authentication, login, paywall,
    /// authorization, and access-control bypass are out of scope in every mode.
    /// </summary>
    UnavailablePublicContent,
}
