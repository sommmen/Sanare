namespace Sanare.Http.Identity.Profiles;

/// <summary>
/// The default, <see cref="AcquisitionMode.Compliance"/> identity: an honest User-Agent that names
/// Sanare and links to it, never a claimed third-party crawler identity (DR-006)
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "The AssistantBrowser profile").
/// </summary>
public sealed class AssistantBrowserProfile : IdentityProfile
{
    /// <summary>The honest, non-configurable User-Agent this profile always sends.</summary>
    public const string UserAgent =
        "Mozilla/5.0 (compatible; Sanare/1.0; +https://github.com/sommmen/Sanare) AssistantBrowser/1.0";

    private const string AcceptHeaderValue =
        "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.9,*/*;q=0.8";

    private static readonly string[] HeaderOrderStatic =
    [
        "User-Agent",
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

    private static readonly string[] AcceptEncodingsStatic = ["gzip", "deflate", "br"];

    public AssistantBrowserProfile()
        : base("AssistantBrowser", isChromiumLineage: false, userAgentMajorVersion: null, HeaderOrderStatic, AcceptEncodingsStatic)
    {
    }

    public override IReadOnlyList<KeyValuePair<string, string>> ComposeHeaders(IdentityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var isTopLevel = request.Navigation == NavigationContext.TopLevel;
        var headers = new List<KeyValuePair<string, string>>(HeaderOrderStatic.Length)
        {
            new("User-Agent", UserAgent),
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
