using AngleSharp.Dom;

namespace Sanare.Http.Identity.Consent;

/// <summary>
/// The known CMP (consent-management-platform) markers used to detect a consent wall, plus the
/// standard "accept necessary" cookie each CMP itself would set
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Consent walls").
/// </summary>
/// <param name="MarkerSelector">A CSS selector (or, for TCF, a script-global check) identifying the CMP's banner markup.</param>
/// <param name="CookieName">The name of the documented, static consent cookie the CMP sets on "accept necessary".</param>
/// <param name="CookieValue">The documented, static value for that cookie.</param>
public sealed record ConsentSignature(string Name, string MarkerSelector, string CookieName, string CookieValue)
{
    /// <summary>OneTrust: banner id <c>#onetrust-banner-sdk</c>; cookie <c>OptanonAlertBoxClosed</c>.</summary>
    /// <remarks>
    /// The real cookie's value is normally a timestamp; a fixed placeholder is used here because
    /// identity composition is deterministic by design (no per-request or per-run values) and
    /// OneTrust accepts any well-formed value as "closed".
    /// </remarks>
    public static readonly ConsentSignature OneTrust = new(
        "OneTrust", "#onetrust-banner-sdk", "OptanonAlertBoxClosed", "1");

    /// <summary>Cookiebot: dialog id <c>#CybotCookiebotDialog</c>; cookie <c>CookieConsent</c>.</summary>
    public static readonly ConsentSignature Cookiebot = new(
        "Cookiebot", "#CybotCookiebotDialog", "CookieConsent", "{stamp:-1,necessary:true,preferences:false,statistics:false,marketing:false,method:'explicit'}");

    /// <summary>IAB TCF: global <c>__tcfapi</c> function marker; cookie <c>euconsent-v2</c> (opaque placeholder consent string).</summary>
    public static readonly ConsentSignature Tcf = new(
        "TCF", "script:__tcfapi", "euconsent-v2", "CPXX");

    /// <summary>The signatures checked for every source, before any per-source override.</summary>
    public static readonly IReadOnlyList<ConsentSignature> Known = [OneTrust, Cookiebot, Tcf];

    /// <summary>
    /// Whether the given document contains this signature's marker. The TCF marker is a
    /// script-global function rather than markup, so it is detected via a literal
    /// <c>__tcfapi(</c> occurrence in an inline script rather than a CSS selector.
    /// </summary>
    public bool MatchesMarker(IDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.Equals(MarkerSelector, "script:__tcfapi", StringComparison.Ordinal))
        {
            foreach (var script in document.QuerySelectorAll("script"))
            {
                if (script.TextContent.Contains("__tcfapi(", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        return document.QuerySelector(MarkerSelector) is not null;
    }
}
