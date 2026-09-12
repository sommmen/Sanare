using System.Text;
using AngleSharp.Html.Parser;
using Sanare.Core.Acquisition;

namespace Sanare.Http.Identity.Consent;

/// <summary>
/// Classifies acquired content as a CAPTCHA challenge or a login/paywall terminal wall
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Scope boundaries", AC-ID-016, AC-ID-017).
/// A pure detector: it returns a classification and nothing else. There is no solver invocation and
/// no bypass path anywhere in this type or the assembly it lives in.
/// </summary>
public sealed class WallClassifier
{
    private static readonly string[] CaptchaSelectors =
    [
        "iframe[src*='recaptcha']",
        "div.g-recaptcha",
        "iframe[src*='hcaptcha']",
        "div.h-captcha",
        "#cf-challenge-running",
    ];

    private static readonly string[] LoginPaywallSelectors =
    [
        "input[type='password']",
        "form[action*='login']",
        "div.paywall",
        "#paywall",
        "[data-paywall]",
    ];

    private readonly HtmlParser _parser = new();

    /// <summary>Classifies the given content. Returns <see cref="WallClassification.None"/> when neither signature set matches.</summary>
    public WallClassification Classify(AcquiredContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var html = Encoding.UTF8.GetString(content.Body.Span);
        var document = _parser.ParseDocument(html);

        foreach (var selector in CaptchaSelectors)
        {
            if (document.QuerySelector(selector) is not null)
            {
                return WallClassification.Challenge;
            }
        }

        foreach (var selector in LoginPaywallSelectors)
        {
            if (document.QuerySelector(selector) is not null)
            {
                return WallClassification.UnavailablePublicContent;
            }
        }

        return WallClassification.None;
    }
}
