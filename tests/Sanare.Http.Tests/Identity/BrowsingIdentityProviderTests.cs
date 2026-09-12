using System.Globalization;
using System.Net;
using System.Text;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Compliance;
using Sanare.Http.Identity.Consent;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Http.Tests.Identity;

public sealed class BrowsingIdentityProviderTests
{
    private static readonly IReadOnlySet<string> SupportedEncodings = new HashSet<string>(StringComparer.Ordinal) { "gzip", "deflate", "br" };

    private static IdentityOptions CreateOptions(
        IReadOnlyDictionary<string, SourceIdentityOverride>? sourceOverrides = null,
        string defaultProfileId = "AssistantBrowser") =>
        new(
            Profiles: [new AssistantBrowserProfile(), new DesktopChromeProfile()],
            DefaultProfileId: defaultProfileId,
            SupportedContentEncodings: SupportedEncodings,
            SourceOverrides: sourceOverrides);

    [Fact]
    public void Constructor_throws_SNR_ID_001_when_a_configured_profile_fails_coherence_validation()
    {
        var incoherentProfile = new IncoherentProfile();
        var options = new IdentityOptions(
            Profiles: [incoherentProfile],
            DefaultProfileId: incoherentProfile.ProfileId,
            SupportedContentEncodings: SupportedEncodings);

        var exception = Assert.Throws<IdentityConfigurationException>(() => new BrowsingIdentityProvider(options));

        Assert.Equal("SNR-ID-001", exception.Code);
    }

    [Fact]
    public void Constructor_throws_SNR_ID_002_when_the_default_profile_id_is_unknown()
    {
        var options = CreateOptions(defaultProfileId: "NoSuchProfile");

        var exception = Assert.Throws<IdentityConfigurationException>(() => new BrowsingIdentityProvider(options));

        Assert.Equal("SNR-ID-002", exception.Code);
    }

    [Fact]
    public void Constructor_throws_SNR_ID_002_when_a_source_override_names_an_unknown_profile()
    {
        var options = CreateOptions(sourceOverrides: new Dictionary<string, SourceIdentityOverride>(StringComparer.Ordinal)
        {
            ["source-a"] = new SourceIdentityOverride(ProfileId: "NoSuchProfile"),
        });

        var exception = Assert.Throws<IdentityConfigurationException>(() => new BrowsingIdentityProvider(options));

        Assert.Equal("SNR-ID-002", exception.Code);
    }

    [Fact]
    public void Constructor_does_not_throw_for_the_built_in_profiles()
    {
        var exception = Record.Exception(() => new BrowsingIdentityProvider(CreateOptions()));
        Assert.Null(exception);
    }

    [Fact]
    public void GetIdentity_composes_headers_from_the_default_profile_when_no_override_exists()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions());
        var request = new IdentityRequest("source-a", new Uri("https://example.test/detail"), CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel);

        var identity = provider.GetIdentity(request);

        Assert.Equal("AssistantBrowser", identity.ProfileId);
        Assert.Contains(identity.Headers, h => h.Key == "User-Agent" && h.Value == AssistantBrowserProfile.UserAgent);
    }

    [Fact]
    public void GetIdentity_uses_the_per_source_override_profile_when_configured()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions(new Dictionary<string, SourceIdentityOverride>(StringComparer.Ordinal)
        {
            ["source-a"] = new SourceIdentityOverride(ProfileId: "DesktopChrome"),
        }));
        var request = new IdentityRequest("source-a", new Uri("https://example.test/detail"), CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel);

        var identity = provider.GetIdentity(request);

        Assert.Equal("DesktopChrome", identity.ProfileId);
    }

    [Fact]
    public void GetIdentity_includes_current_jar_cookies_for_the_target_host()
    {
        var jar = new HostCookieJar();
        jar.Set("example.test", new Cookie("consent", "1", "/", "example.test"));
        var provider = new BrowsingIdentityProvider(CreateOptions(), cookieJar: jar);
        var request = new IdentityRequest("source-a", new Uri("https://example.test/detail"), CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel);

        var identity = provider.GetIdentity(request);

        Assert.Contains(identity.Cookies, c => c.Name == "consent" && c.Value == "1");
    }

    [Fact]
    public void GetIdentity_records_a_request_against_the_compliance_report()
    {
        var reporter = new ComplianceReporter();
        var provider = new BrowsingIdentityProvider(CreateOptions(), complianceReporter: reporter);
        var request = new IdentityRequest("source-a", new Uri("https://example.test/detail"), CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel);

        provider.GetIdentity(request);
        provider.GetIdentity(request);

        Assert.Equal(2, provider.GetComplianceReport("source-a").RequestCount);
    }

    [Fact]
    public void GetIdentity_is_deterministic_across_many_repeated_calls_for_the_same_inputs()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions());
        var request = new IdentityRequest("source-a", new Uri("https://example.test/detail"), CultureInfo.GetCultureInfo("nl-NL"), NavigationContext.TopLevel);

        var first = provider.GetIdentity(request);
        for (var i = 0; i < 1_000; i++)
        {
            var next = provider.GetIdentity(request);
            Assert.Equal(first.ProfileId, next.ProfileId);
            Assert.Equal(first.Headers, next.Headers);
        }
    }

    private static IReadOnlyDictionary<string, SourceIdentityOverride> ProductDetailOverride() =>
        new Dictionary<string, SourceIdentityOverride>(StringComparer.Ordinal)
        {
            ["source-a"] = new SourceIdentityOverride(ExpectedContentRootSelector: "#product-detail"),
        };

    [Fact]
    public void EvaluateConsent_returns_not_detected_when_no_wall_signature_is_present()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions(ProductDetailOverride()));
        var content = LoadFixture("lenovo-tablet-product.html");

        var decision = provider.EvaluateConsent(content, "source-a");

        Assert.False(decision.WallDetected);
    }

    [Fact]
    public void EvaluateConsent_sets_the_consent_cookie_in_the_jar_when_a_wall_is_detected()
    {
        var jar = new HostCookieJar();
        var provider = new BrowsingIdentityProvider(CreateOptions(ProductDetailOverride()), cookieJar: jar);
        var content = LoadFixture("consent-wall-onetrust.html");

        var decision = provider.EvaluateConsent(content, "source-a");

        Assert.True(decision.WallDetected);
        Assert.Contains(jar.Get("example.test"), c => c.Name == "OptanonAlertBoxClosed");
    }

    [Fact]
    public void EvaluateConsent_marks_the_retry_flag_after_a_detected_wall_and_clears_it_once_resolved()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions(ProductDetailOverride()));
        var wallContent = LoadFixture("consent-wall-onetrust.html");
        var clearContent = LoadFixture("lenovo-tablet-product.html");

        var firstDecision = provider.EvaluateConsent(wallContent, "source-a");
        Assert.False(firstDecision.RetryAttempted);

        var secondDecision = provider.EvaluateConsent(wallContent, "source-a");
        Assert.True(secondDecision.RetryAttempted);

        var thirdDecision = provider.EvaluateConsent(clearContent, "source-a");
        Assert.False(thirdDecision.WallDetected);

        // The retry flag having cleared is only observable indirectly: a subsequent wall
        // detection reports RetryAttempted=false again, as if this were a fresh encounter.
        var fourthDecision = provider.EvaluateConsent(wallContent, "source-a");
        Assert.False(fourthDecision.RetryAttempted);
    }

    [Fact]
    public void GetComplianceReport_returns_the_report_for_the_given_source()
    {
        var provider = new BrowsingIdentityProvider(CreateOptions(new Dictionary<string, SourceIdentityOverride>(StringComparer.Ordinal)
        {
            ["source-a"] = new SourceIdentityOverride(ProfileId: "DesktopChrome", Mode: AcquisitionMode.Stealth),
        }));

        var report = provider.GetComplianceReport("source-a");

        Assert.Equal("DesktopChrome", report.IdentityProfileId);
        Assert.Equal(AcquisitionMode.Stealth, report.Mode);
    }

    private static AcquiredContent LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        var bytes = File.ReadAllBytes(path);
        var url = new Uri("https://example.test/product");
        return new AcquiredContent(url, url, 200, "text/html", Encoding.UTF8, bytes, new Dictionary<string, string>(), ContentOrigin.Network, null, TimeSpan.Zero);
    }

    /// <summary>A profile that emits a header not present in its own declared header order, tripping rule 6.</summary>
    private sealed class IncoherentProfile() : IdentityProfile("Incoherent", isChromiumLineage: false, userAgentMajorVersion: null, headerOrder: ["User-Agent"], acceptEncodings: ["gzip"])
    {
        public override IReadOnlyList<KeyValuePair<string, string>> ComposeHeaders(IdentityRequest request) =>
            [new("User-Agent", "Test/1.0"), new("X-Undeclared", "value")];
    }
}
