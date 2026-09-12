using System.Text;
using Sanare.Core.Acquisition;
using Sanare.Http.Identity.Consent;

namespace Sanare.Http.Tests.Identity.Consent;

public sealed class WallClassifierTests
{
    private static readonly Uri Url = new("https://example.test/product");

    [Fact]
    public void Classify_detects_a_captcha_challenge()
    {
        var classifier = new WallClassifier();
        var content = LoadFixture("captcha-challenge.html");

        Assert.Equal(WallClassification.Challenge, classifier.Classify(content));
    }

    [Fact]
    public void Classify_detects_a_login_wall()
    {
        var classifier = new WallClassifier();
        var content = LoadFixture("login-wall.html");

        Assert.Equal(WallClassification.UnavailablePublicContent, classifier.Classify(content));
    }

    [Fact]
    public void Classify_returns_none_for_ordinary_product_content()
    {
        var classifier = new WallClassifier();
        var content = LoadFixture("lenovo-tablet-product.html");

        Assert.Equal(WallClassification.None, classifier.Classify(content));
    }

    [Fact]
    public void Classify_returns_none_for_a_pure_consent_wall_with_no_captcha_or_login_markers()
    {
        var classifier = new WallClassifier();
        var content = LoadFixture("consent-wall-onetrust.html");

        Assert.Equal(WallClassification.None, classifier.Classify(content));
    }

    private static AcquiredContent LoadFixture(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", fileName);
        var bytes = File.ReadAllBytes(path);
        return new AcquiredContent(Url, Url, 200, "text/html", Encoding.UTF8, bytes, new Dictionary<string, string>(), ContentOrigin.Network, null, TimeSpan.Zero);
    }
}
