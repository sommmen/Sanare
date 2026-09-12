namespace Sanare.Http.Identity.Profiles;

/// <summary>
/// The browser-tier identity: declared here so its coherence rules and header order are validated
/// alongside every other profile, but not selectable by the HTTP tier. <c>browser-tier</c> (#8) owns
/// pairing this profile with Playwright's actual Chromium engine — a mismatch between the UA string
/// and the real engine's TLS/HTTP behaviour would itself be incoherent
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "The AssistantBrowser profile").
/// </summary>
public sealed class DesktopChromeProfile : IdentityProfile
{
    /// <summary>The UA major version this profile's <c>Sec-CH-UA*</c> headers must agree with (rule 4).</summary>
    public const int ChromeMajorVersion = 130;

    private static readonly string[] HeaderOrderStatic =
    [
        "User-Agent",
        "Sec-CH-UA",
        "Sec-CH-UA-Mobile",
        "Sec-CH-UA-Platform",
        "Accept",
        "Accept-Language",
        "Accept-Encoding",
        "Sec-Fetch-Site",
        "Sec-Fetch-Mode",
        "Sec-Fetch-Dest",
        "Sec-Fetch-User",
        "Upgrade-Insecure-Requests",
        "Referer",
    ];

    private const string AcceptHeaderValue =
        "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8";

    private static readonly string[] AcceptEncodingsStatic = ["gzip", "deflate", "br"];

    public DesktopChromeProfile()
        : base("DesktopChrome", isChromiumLineage: true, userAgentMajorVersion: ChromeMajorVersion, HeaderOrderStatic, AcceptEncodingsStatic)
    {
    }

    /// <summary>
    /// Composes the fixed, non-randomised header set for this profile. <c>browser-tier</c> is
    /// expected to source the actual <c>User-Agent</c>/<c>Sec-CH-UA*</c> values from the Playwright
    /// context it launches rather than this method, so the two never drift apart; this
    /// implementation exists so the profile can still be validated and unit-tested in isolation at
    /// the HTTP tier.
    /// </summary>
    public override IReadOnlyList<KeyValuePair<string, string>> ComposeHeaders(IdentityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isTopLevel = request.Navigation == NavigationContext.TopLevel;
        var headers = new List<KeyValuePair<string, string>>(HeaderOrderStatic.Length)
        {
            new("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromeMajorVersion}.0.0.0 Safari/537.36"),
            new("Sec-CH-UA", $"\"Chromium\";v=\"{ChromeMajorVersion}\", \"Not_A Brand\";v=\"24\""),
            new("Sec-CH-UA-Mobile", "?0"),
            new("Sec-CH-UA-Platform", "\"Windows\""),
            new("Accept", AcceptHeaderValue),
            new("Accept-Language", ComposeAcceptLanguage(request.Culture)),
            new("Accept-Encoding", string.Join(", ", AcceptEncodingsStatic)),
            new("Sec-Fetch-Site", isTopLevel ? "none" : "same-origin"),
            new("Sec-Fetch-Mode", "navigate"),
            new("Sec-Fetch-Dest", "document"),
        };
        if (isTopLevel)
        {
            headers.Add(new("Sec-Fetch-User", "?1"));
        }

        headers.Add(new("Upgrade-Insecure-Requests", "1"));

        var referer = ComposeReferer(request);
        if (referer is not null)
        {
            headers.Add(new("Referer", referer));
        }

        return headers;
    }
}
