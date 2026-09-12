using System.Text;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Consent;

namespace Sanare.Http.Tests.Identity.Consent;

public sealed class ConsentPolicyTests
{
    private static readonly Uri Url = new("https://example.test/product");

    [Theory]
    [InlineData("consent-wall-onetrust.html", "OneTrust", "OptanonAlertBoxClosed")]
    [InlineData("consent-wall-cookiebot.html", "Cookiebot", "CookieConsent")]
    [InlineData("consent-wall-tcf.html", "TCF", "euconsent-v2")]
    public void Evaluate_detects_a_known_cmp_signature_when_the_content_root_is_missing(string fixtureFile, string expectedSignature, string expectedCookieName)
    {
        var policy = new ConsentPolicy();
        var content = LoadFixture(fixtureFile);

        var decision = policy.Evaluate(content, "#product-detail", consentSpec: null, retryAttempted: false);

        Assert.True(decision.WallDetected);
        Assert.Equal(expectedSignature, decision.DetectedSignatureName);
        Assert.Equal(expectedCookieName, decision.CookieName);
        Assert.NotNull(decision.CookieValue);
    }

    [Fact]
    public void Evaluate_does_not_detect_a_wall_when_the_expected_content_root_is_present()
    {
        // The Lenovo fixture ships a (dismissed) OneTrust marker but has already rendered its
        // product content — the false-positive guard (docs "Consent walls") must win.
        var policy = new ConsentPolicy();
        var content = LoadFixture("lenovo-tablet-product.html");

        var decision = policy.Evaluate(content, "#product-detail", consentSpec: null, retryAttempted: false);

        Assert.False(decision.WallDetected);
        Assert.Same(ConsentDecision.NotDetected, decision);
    }

    [Fact]
    public void Evaluate_does_not_detect_a_wall_when_no_known_signature_matches()
    {
        var policy = new ConsentPolicy();
        var content = LoadFixture("login-wall.html");

        var decision = policy.Evaluate(content, "#product-detail", consentSpec: null, retryAttempted: false);

        Assert.False(decision.WallDetected);
    }

    [Fact]
    public void Evaluate_uses_the_per_source_cookie_override_name_when_a_signature_matches()
    {
        var policy = new ConsentPolicy();
        var content = LoadFixture("consent-wall-onetrust.html");
        var consentSpec = new ConsentSpec("cookie", "custom-consent-cookie");

        var decision = policy.Evaluate(content, "#product-detail", consentSpec, retryAttempted: false);

        Assert.True(decision.WallDetected);
        Assert.Equal("OneTrust", decision.DetectedSignatureName);
        Assert.Equal("custom-consent-cookie", decision.CookieName);
    }

    [Fact]
    public void Evaluate_propagates_the_retry_attempted_flag_into_the_decision()
    {
        var policy = new ConsentPolicy();
        var content = LoadFixture("consent-wall-onetrust.html");

        var decision = policy.Evaluate(content, "#product-detail", consentSpec: null, retryAttempted: true);

        Assert.True(decision.RetryAttempted);
    }

    private static AcquiredContent LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        var bytes = File.ReadAllBytes(path);
        return new AcquiredContent(Url, Url, 200, "text/html", Encoding.UTF8, bytes, new Dictionary<string, string>(), ContentOrigin.Network, null, TimeSpan.Zero);
    }
}
