using System.Globalization;

namespace Sanare.Http.Identity.Profiles;

/// <summary>
/// Validates an <see cref="IdentityProfile"/> against the six coherence rules
/// (docs/features/browsing-identity.md, "Key Behaviors" &gt; "Coherence rules"), one method per rule,
/// each returning the named failures it found rather than a bare <see cref="bool"/> so an
/// <c>SNR-ID-001</c> diagnostic can name the rule and the offending header. Runs against a small set
/// of representative sample requests rather than a single fixed one, so rules that only manifest for
/// specific navigation contexts or cultures are still exercised.
/// </summary>
/// <param name="supportedContentEncodings">
/// The content-encodings the transport can actually decode. Passed in — rather than hard-coded to
/// <c>gzip, deflate, br</c> — so rule 1 fails if a profile claims an encoding the transport cannot
/// decode.
/// </param>
public sealed class ProfileCoherenceValidator(IReadOnlySet<string> supportedContentEncodings)
{
    private static readonly Uri SampleOrigin = new("https://example.test/lister");
    private static readonly Uri SampleTarget = new("https://example.test/detail");
    private static readonly Uri SampleCrossOriginReferrer = new("https://other.test/lister");

    private static readonly IReadOnlyList<IdentityRequest> SampleRequests =
    [
        new("sample", SampleTarget, CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel),
        new("sample", SampleTarget, CultureInfo.GetCultureInfo("en-US"), NavigationContext.SameOriginSubResource),
        new("sample", SampleTarget, CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.DetailFromLister, ReferrerUri: SampleOrigin),
        new("sample", SampleTarget, CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.DetailFromLister, ReferrerUri: SampleCrossOriginReferrer),
    ];

    private readonly IReadOnlySet<string> _supportedContentEncodings = supportedContentEncodings ?? throw new ArgumentNullException(nameof(supportedContentEncodings));

    /// <summary>Runs all six rules and returns every failure found (empty when the profile is coherent).</summary>
    public IReadOnlyList<ProfileCoherenceFailure> Validate(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        failures.AddRange(ValidateAcceptEncoding(profile));
        failures.AddRange(ValidateAcceptLanguage(profile));
        failures.AddRange(ValidateSecFetchUser(profile));
        failures.AddRange(ValidateSecChUa(profile));
        failures.AddRange(ValidateReferer(profile));
        failures.AddRange(ValidateHeaderOrder(profile));
        return failures;
    }

    /// <summary>Rule 1 — <c>Accept-Encoding</c> must list only encodings the transport can actually decode.</summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateAcceptEncoding(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var encoding in profile.AcceptEncodings)
        {
            if (!_supportedContentEncodings.Contains(encoding))
            {
                failures.Add(new ProfileCoherenceFailure(1, profile.ProfileId, "Accept-Encoding",
                    $"Profile claims content-encoding '{encoding}', which the transport cannot decode."));
            }
        }

        return failures;
    }

    /// <summary>Rule 2 — <c>Accept-Language</c> must contain the source culture's language as the highest-weighted entry.</summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateAcceptLanguage(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var request in SampleRequests)
        {
            var headers = profile.ComposeHeaders(request);
            var value = FindHeaderValue(headers, "Accept-Language");
            if (value is null)
            {
                failures.Add(new ProfileCoherenceFailure(2, profile.ProfileId, "Accept-Language", "Profile did not emit Accept-Language."));
                continue;
            }

            var firstEntry = value.Split(',', 2)[0];
            var expectedLanguage = request.Culture.TwoLetterISOLanguageName;
            var matches = firstEntry.StartsWith(request.Culture.Name, StringComparison.OrdinalIgnoreCase)
                || firstEntry.StartsWith(expectedLanguage, StringComparison.OrdinalIgnoreCase);
            if (!matches)
            {
                failures.Add(new ProfileCoherenceFailure(2, profile.ProfileId, "Accept-Language",
                    $"Highest-weighted entry '{firstEntry}' does not match culture '{request.Culture.Name}'."));
            }
        }

        return failures;
    }

    /// <summary>Rule 3 — <c>Sec-Fetch-User: ?1</c> may appear only with <c>navigate</c>/<c>document</c>.</summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateSecFetchUser(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var request in SampleRequests)
        {
            var headers = profile.ComposeHeaders(request);
            var secFetchUser = FindHeaderValue(headers, "Sec-Fetch-User");
            if (secFetchUser is null) { continue; }
            if (!string.Equals(secFetchUser, "?1", StringComparison.Ordinal)) { continue; }

            var mode = FindHeaderValue(headers, "Sec-Fetch-Mode");
            var dest = FindHeaderValue(headers, "Sec-Fetch-Dest");
            if (!string.Equals(mode, "navigate", StringComparison.Ordinal) || !string.Equals(dest, "document", StringComparison.Ordinal))
            {
                failures.Add(new ProfileCoherenceFailure(3, profile.ProfileId, "Sec-Fetch-User",
                    $"Sec-Fetch-User: ?1 was emitted alongside Sec-Fetch-Mode='{mode}', Sec-Fetch-Dest='{dest}'."));
            }
        }

        return failures;
    }

    /// <summary>
    /// Rule 4 — <c>Sec-CH-UA*</c> headers may appear only for profiles declaring Chromium lineage,
    /// and must agree with the UA string's major version.
    /// </summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateSecChUa(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var request in SampleRequests)
        {
            var headers = profile.ComposeHeaders(request);
            var secChUa = FindHeaderValue(headers, "Sec-CH-UA");
            if (secChUa is null) { continue; }

            if (!profile.IsChromiumLineage)
            {
                failures.Add(new ProfileCoherenceFailure(4, profile.ProfileId, "Sec-CH-UA",
                    "Sec-CH-UA was emitted by a profile that does not declare Chromium lineage."));
                continue;
            }

            var userAgent = FindHeaderValue(headers, "User-Agent") ?? string.Empty;
            var expectedVersion = profile.UserAgentMajorVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (expectedVersion is null || !secChUa.Contains($"v=\"{expectedVersion}\"", StringComparison.Ordinal)
                || !userAgent.Contains($"Chrome/{expectedVersion}.", StringComparison.Ordinal))
            {
                failures.Add(new ProfileCoherenceFailure(4, profile.ProfileId, "Sec-CH-UA",
                    $"Sec-CH-UA ('{secChUa}') does not agree with the User-Agent major version ({expectedVersion})."));
            }
        }

        return failures;
    }

    /// <summary>
    /// Rule 5 — <c>Referer</c> is emitted only for a <see cref="NavigationContext.DetailFromLister"/>
    /// context, only same-origin, and only with the URL the run actually visited.
    /// </summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateReferer(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var request in SampleRequests)
        {
            var headers = profile.ComposeHeaders(request);
            var referer = FindHeaderValue(headers, "Referer");
            if (referer is null) { continue; }

            if (request.Navigation != NavigationContext.DetailFromLister)
            {
                failures.Add(new ProfileCoherenceFailure(5, profile.ProfileId, "Referer",
                    $"Referer was emitted for navigation context '{request.Navigation}', not DetailFromLister."));
                continue;
            }

            if (request.ReferrerUri is null || !string.Equals(request.ReferrerUri.Host, request.TargetUri.Host, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add(new ProfileCoherenceFailure(5, profile.ProfileId, "Referer",
                    "Referer was emitted without a same-origin caller-supplied referrer."));
                continue;
            }

            if (!string.Equals(referer, request.ReferrerUri.AbsoluteUri, StringComparison.Ordinal))
            {
                failures.Add(new ProfileCoherenceFailure(5, profile.ProfileId, "Referer",
                    "Referer value does not match the caller-supplied referrer verbatim (possible fabrication)."));
            }
        }

        return failures;
    }

    /// <summary>Rule 6 — header order is fixed per profile and asserted.</summary>
    public IReadOnlyList<ProfileCoherenceFailure> ValidateHeaderOrder(IdentityProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileCoherenceFailure> failures = [];
        foreach (var request in SampleRequests)
        {
            var headers = profile.ComposeHeaders(request);
            var lastIndex = -1;
            foreach (var header in headers)
            {
                var declaredIndex = IndexOf(profile.HeaderOrder, header.Key);
                if (declaredIndex < 0)
                {
                    failures.Add(new ProfileCoherenceFailure(6, profile.ProfileId, header.Key,
                        "Header was emitted but is not present in the profile's declared header order."));
                    continue;
                }

                if (declaredIndex <= lastIndex)
                {
                    failures.Add(new ProfileCoherenceFailure(6, profile.ProfileId, header.Key,
                        "Header was emitted out of the profile's declared fixed order."));
                    continue;
                }

                lastIndex = declaredIndex;
            }
        }

        return failures;
    }

    private static string? FindHeaderValue(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)) { return header.Value; }
        }

        return null;
    }

    private static int IndexOf(IReadOnlyList<string> values, string value)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) { return i; }
        }

        return -1;
    }
}
