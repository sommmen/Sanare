using System.Globalization;

namespace Sanare.Http.Identity.Profiles;

/// <summary>
/// A named, coherent set of request-identity rules. Every profile declares its own fixed header
/// order and claimed capabilities up front so <see cref="ProfileCoherenceValidator"/> can validate
/// it once, at startup, rather than the header composition being checked ad hoc per request
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Coherence rules").
/// </summary>
public abstract class IdentityProfile
{
    protected IdentityProfile(
        string profileId,
        bool isChromiumLineage,
        int? userAgentMajorVersion,
        IReadOnlyList<string> headerOrder,
        IReadOnlyList<string> acceptEncodings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(headerOrder);
        ArgumentNullException.ThrowIfNull(acceptEncodings);
        if (isChromiumLineage && userAgentMajorVersion is null)
        {
            throw new ArgumentException("A Chromium-lineage profile must declare a UA major version.", nameof(userAgentMajorVersion));
        }

        ProfileId = profileId;
        IsChromiumLineage = isChromiumLineage;
        UserAgentMajorVersion = userAgentMajorVersion;
        HeaderOrder = headerOrder;
        AcceptEncodings = acceptEncodings;
    }

    /// <summary>Stable identifier for this profile (e.g. <c>"AssistantBrowser"</c>).</summary>
    public string ProfileId { get; }

    /// <summary>Whether this profile's User-Agent claims Chromium lineage (gates <c>Sec-CH-UA*</c>, rule 4).</summary>
    public bool IsChromiumLineage { get; }

    /// <summary>The UA major version, when <see cref="IsChromiumLineage"/> is <see langword="true"/>.</summary>
    public int? UserAgentMajorVersion { get; }

    /// <summary>
    /// The fixed relative order of header names this profile may emit. A shuffled order is a cheap
    /// bot tell (rule 6), so this is asserted, not merely documented.
    /// </summary>
    public IReadOnlyList<string> HeaderOrder { get; }

    /// <summary>
    /// The content-encodings this profile's <c>Accept-Encoding</c> claims support for. Validated
    /// against the transport's actually-enabled decoders (rule 1) rather than assumed correct.
    /// </summary>
    public IReadOnlyList<string> AcceptEncodings { get; }

    /// <summary>Composes the ordered header list for one request under this profile.</summary>
    public abstract IReadOnlyList<KeyValuePair<string, string>> ComposeHeaders(IdentityRequest request);

    /// <summary>
    /// Composes <c>Accept-Language</c> from the request's culture as
    /// <c>{culture},{lang};q=0.9,en;q=0.8</c>, collapsing the duplicate <c>en</c> entry when the
    /// culture is already English.
    /// </summary>
    protected static string ComposeAcceptLanguage(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var lang = culture.TwoLetterISOLanguageName;
        if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(culture.Name, lang, StringComparison.OrdinalIgnoreCase)
                ? $"{lang};q=0.9"
                : $"{culture.Name},{lang};q=0.9";
        }

        return string.Equals(culture.Name, lang, StringComparison.OrdinalIgnoreCase)
            ? $"{lang};q=0.9,en;q=0.8"
            : $"{culture.Name},{lang};q=0.9,en;q=0.8";
    }

    /// <summary>
    /// Resolves the Referer to emit, honouring coherence rule 5: only for a <see cref="NavigationContext.DetailFromLister"/>
    /// context, only same-origin, and only from a caller-supplied referrer — never fabricated.
    /// </summary>
    protected static string? ComposeReferer(IdentityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Navigation != NavigationContext.DetailFromLister) { return null; }
        if (request.ReferrerUri is null) { return null; }
        if (!string.Equals(request.ReferrerUri.Host, request.TargetUri.Host, StringComparison.OrdinalIgnoreCase)) { return null; }
        return request.ReferrerUri.AbsoluteUri;
    }
}
