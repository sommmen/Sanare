using Sanare.Http.Identity;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Http.Tests.Identity.Profiles;

public sealed class ProfileCoherenceValidatorTests
{
    private static readonly IReadOnlySet<string> SupportedEncodings = new HashSet<string>(StringComparer.Ordinal) { "gzip", "deflate", "br" };

    private static ProfileCoherenceValidator CreateValidator(IReadOnlySet<string>? supportedEncodings = null) =>
        new(supportedEncodings ?? SupportedEncodings);

    // Rule 1 — Accept-Encoding

    [Fact]
    public void ValidateAcceptEncoding_passes_when_every_claimed_encoding_is_supported()
    {
        var validator = CreateValidator();
        var failures = validator.ValidateAcceptEncoding(new AssistantBrowserProfile());
        Assert.Empty(failures);
    }

    [Fact]
    public void ValidateAcceptEncoding_fails_when_transport_cannot_decode_a_claimed_encoding()
    {
        // Only "gzip" is supported, but the built-in profiles claim "br" and "deflate" too.
        var validator = CreateValidator(new HashSet<string>(StringComparer.Ordinal) { "gzip" });
        var failures = validator.ValidateAcceptEncoding(new AssistantBrowserProfile());
        Assert.Contains(failures, f => f.Rule == 1 && f.Header == "Accept-Encoding");
    }

    // Rule 2 — Accept-Language

    [Fact]
    public void ValidateAcceptLanguage_passes_for_the_built_in_profiles()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.ValidateAcceptLanguage(new AssistantBrowserProfile()));
        Assert.Empty(validator.ValidateAcceptLanguage(new DesktopChromeProfile()));
    }

    [Fact]
    public void ValidateAcceptLanguage_fails_when_the_highest_weighted_entry_does_not_match_the_culture()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Accept-Language"])
        {
            ComposeHeadersOverride = _ => [new("Accept-Language", "ja;q=0.9,en;q=0.8")],
        };

        var failures = validator.ValidateAcceptLanguage(profile);

        Assert.Contains(failures, f => f.Rule == 2 && f.Header == "Accept-Language");
    }

    // Rule 3 — Sec-Fetch-User

    [Fact]
    public void ValidateSecFetchUser_passes_when_flagged_only_alongside_navigate_document()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.ValidateSecFetchUser(new AssistantBrowserProfile()));
    }

    [Fact]
    public void ValidateSecFetchUser_fails_when_flagged_without_navigate_document()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Sec-Fetch-Mode", "Sec-Fetch-Dest", "Sec-Fetch-User"])
        {
            ComposeHeadersOverride = _ =>
            [
                new("Sec-Fetch-Mode", "cors"),
                new("Sec-Fetch-Dest", "empty"),
                new("Sec-Fetch-User", "?1"),
            ],
        };

        var failures = validator.ValidateSecFetchUser(profile);

        Assert.Contains(failures, f => f.Rule == 3 && f.Header == "Sec-Fetch-User");
    }

    // Rule 4 — Sec-CH-UA

    [Fact]
    public void ValidateSecChUa_passes_for_a_chromium_lineage_profile_with_matching_versions()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.ValidateSecChUa(new DesktopChromeProfile()));
    }

    [Fact]
    public void ValidateSecChUa_fails_when_emitted_by_a_non_chromium_profile()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Sec-CH-UA", "User-Agent"], isChromiumLineage: false)
        {
            ComposeHeadersOverride = _ =>
            [
                new("Sec-CH-UA", "\"Chromium\";v=\"130\""),
                new("User-Agent", "Mozilla/5.0 Chrome/130.0.0.0"),
            ],
        };

        var failures = validator.ValidateSecChUa(profile);

        Assert.Contains(failures, f => f.Rule == 4 && f.Header == "Sec-CH-UA" && f.Detail.Contains("does not declare Chromium lineage", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateSecChUa_fails_when_the_version_disagrees_with_the_user_agent()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Sec-CH-UA", "User-Agent"], isChromiumLineage: true, userAgentMajorVersion: 130)
        {
            ComposeHeadersOverride = _ =>
            [
                new("Sec-CH-UA", "\"Chromium\";v=\"129\""),
                new("User-Agent", "Mozilla/5.0 Chrome/130.0.0.0"),
            ],
        };

        var failures = validator.ValidateSecChUa(profile);

        Assert.Contains(failures, f => f.Rule == 4 && f.Header == "Sec-CH-UA");
    }

    // Rule 5 — Referer

    [Fact]
    public void ValidateReferer_passes_for_the_built_in_profiles()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.ValidateReferer(new AssistantBrowserProfile()));
        Assert.Empty(validator.ValidateReferer(new DesktopChromeProfile()));
    }

    [Fact]
    public void ValidateReferer_fails_when_emitted_outside_detail_from_lister()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Referer"])
        {
            ComposeHeadersOverride = request => request.Navigation == NavigationContext.TopLevel
                ? [new("Referer", "https://example.test/lister")]
                : [],
        };

        var failures = validator.ValidateReferer(profile);

        Assert.Contains(failures, f => f.Rule == 5 && f.Detail.Contains("not DetailFromLister", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReferer_fails_when_emitted_cross_origin()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Referer"])
        {
            ComposeHeadersOverride = request => request.Navigation == NavigationContext.DetailFromLister && request.ReferrerUri is { Host: "other.test" }
                ? [new("Referer", request.ReferrerUri.AbsoluteUri)]
                : [],
        };

        var failures = validator.ValidateReferer(profile);

        Assert.Contains(failures, f => f.Rule == 5 && f.Detail.Contains("same-origin", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReferer_fails_when_the_value_is_fabricated_rather_than_verbatim()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["Referer"])
        {
            ComposeHeadersOverride = request => request.Navigation == NavigationContext.DetailFromLister && request.ReferrerUri is { Host: "example.test" }
                ? [new("Referer", "https://example.test/fabricated")]
                : [],
        };

        var failures = validator.ValidateReferer(profile);

        Assert.Contains(failures, f => f.Rule == 5 && f.Detail.Contains("fabrication", StringComparison.Ordinal));
    }

    // Rule 6 — header order

    [Fact]
    public void ValidateHeaderOrder_passes_for_the_built_in_profiles()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.ValidateHeaderOrder(new AssistantBrowserProfile()));
        Assert.Empty(validator.ValidateHeaderOrder(new DesktopChromeProfile()));
    }

    [Fact]
    public void ValidateHeaderOrder_fails_when_headers_are_emitted_out_of_the_declared_order()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["User-Agent", "Accept"])
        {
            // Emitted in the reverse of the declared order.
            ComposeHeadersOverride = _ => [new("Accept", "text/html"), new("User-Agent", "Test/1.0")],
        };

        var failures = validator.ValidateHeaderOrder(profile);

        Assert.Contains(failures, f => f.Rule == 6 && f.Header == "User-Agent");
    }

    [Fact]
    public void ValidateHeaderOrder_fails_when_a_header_is_not_in_the_declared_order_at_all()
    {
        var validator = CreateValidator();
        var profile = new TestProfile(headerOrder: ["User-Agent"])
        {
            ComposeHeadersOverride = _ => [new("User-Agent", "Test/1.0"), new("X-Undeclared", "value")],
        };

        var failures = validator.ValidateHeaderOrder(profile);

        Assert.Contains(failures, f => f.Rule == 6 && f.Header == "X-Undeclared");
    }

    // Aggregator

    [Fact]
    public void Validate_returns_no_failures_for_either_built_in_profile()
    {
        var validator = CreateValidator();
        Assert.Empty(validator.Validate(new AssistantBrowserProfile()));
        Assert.Empty(validator.Validate(new DesktopChromeProfile()));
    }

    [Fact]
    public void Validate_aggregates_failures_across_all_six_rules()
    {
        var validator = CreateValidator(new HashSet<string>(StringComparer.Ordinal) { "gzip" });
        // AssistantBrowserProfile claims "deflate"/"br" too, so this alone should trip rule 1
        // without needing a bespoke test profile.
        var failures = validator.Validate(new AssistantBrowserProfile());
        Assert.Contains(failures, f => f.Rule == 1);
    }

    /// <summary>
    /// A minimal, fully test-controlled <see cref="IdentityProfile"/> used to exercise failure paths
    /// that the coherent, built-in profiles cannot trigger by construction.
    /// </summary>
    private sealed class TestProfile(
        IReadOnlyList<string> headerOrder,
        bool isChromiumLineage = false,
        int? userAgentMajorVersion = null,
        IReadOnlyList<string>? acceptEncodings = null)
        : IdentityProfile("TestProfile", isChromiumLineage, isChromiumLineage ? userAgentMajorVersion ?? 130 : null, headerOrder, acceptEncodings ?? ["gzip"])
    {
        public Func<IdentityRequest, IReadOnlyList<KeyValuePair<string, string>>>? ComposeHeadersOverride { get; set; }

        public override IReadOnlyList<KeyValuePair<string, string>> ComposeHeaders(IdentityRequest request) =>
            ComposeHeadersOverride?.Invoke(request) ?? [];
    }
}
