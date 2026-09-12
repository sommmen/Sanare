using System.Text;
using AngleSharp.Html.Parser;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;

namespace Sanare.Http.Identity.Consent;

/// <summary>
/// The default <see cref="IConsentPolicy"/>: signature-based detection against the known CMP markers
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Consent walls").
/// </summary>
public sealed class ConsentPolicy : IConsentPolicy
{
    private readonly HtmlParser _parser = new();

    public ConsentDecision Evaluate(AcquiredContent content, string expectedContentRootSelector, ConsentSpec? consentSpec, bool retryAttempted)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedContentRootSelector);

        var html = Encoding.UTF8.GetString(content.Body.Span);
        var document = _parser.ParseDocument(html);
        var contentRootPresent = document.QuerySelector(expectedContentRootSelector) is not null;
        if (contentRootPresent)
        {
            return ConsentDecision.NotDetected;
        }

        var signature = FindMatchingSignature(document, consentSpec);
        if (signature is null)
        {
            return ConsentDecision.NotDetected;
        }

        return new ConsentDecision(
            WallDetected: true,
            DetectedSignatureName: signature.Name,
            CookieName: signature.CookieName,
            CookieValue: signature.CookieValue,
            RetryAttempted: retryAttempted);
    }

    private ConsentSignature? FindMatchingSignature(AngleSharp.Dom.IDocument document, ConsentSpec? consentSpec)
    {
        if (consentSpec is { Strategy: "cookie", Name: { Length: > 0 } cookieName })
        {
            // A per-source override names only the cookie to set on match; the marker detection
            // still runs against the known signatures, and the override's cookie name replaces
            // whichever signature's default would otherwise be used.
            foreach (var known in ConsentSignature.Known)
            {
                if (known.MatchesMarker(document))
                {
                    return known with { CookieName = cookieName };
                }
            }

            return null;
        }

        foreach (var known in ConsentSignature.Known)
        {
            if (known.MatchesMarker(document))
            {
                return known;
            }
        }

        return null;
    }
}
