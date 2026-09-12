namespace Sanare.Http.Identity;

/// <summary>
/// A source's explicit, audited acquisition posture (docs/sanare/tech-design.md §11.4;
/// docs/features/browsing-identity.md, "Purpose"). Every source has one; there is no implicit mode.
/// </summary>
public enum AcquisitionMode
{
    /// <summary>
    /// The default posture: identity honestly names Sanare, and applicable <c>robots.txt</c>
    /// <c>Disallow</c> rules are enforced before any request is sent.
    /// </summary>
    Compliance,

    /// <summary>
    /// An explicit, per-source, audited posture that may request publicly accessible disallowed
    /// paths through the normal governed pipeline. Never changes the traffic-protection invariants
    /// (no randomisation, no automatic escalation, no credential-shaped capability).
    /// </summary>
    Stealth,
}
