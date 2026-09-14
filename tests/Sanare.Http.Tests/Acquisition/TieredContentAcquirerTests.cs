using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Acquisition;

namespace Sanare.Http.Tests.Acquisition;

public sealed class TieredContentAcquirerTests
{
    private static readonly Uri Target = new("https://example.test/products/1");

    [Theory]
    [InlineData(AcquisitionTier.JsonApi)]
    [InlineData(AcquisitionTier.StructuredData)]
    [InlineData(AcquisitionTier.Html)]
    public async Task Non_browser_tiers_are_routed_to_the_http_acquirer(AcquisitionTier tier)
    {
        var http = new RecordingAcquirer(Content());
        var browser = new RecordingAcquirer(Content());
        var acquirer = new TieredContentAcquirer(http, browser);

        await acquirer.AcquireAsync(Request(tier));

        Assert.Single(http.Requests);
        Assert.Empty(browser.Requests);
    }

    [Fact]
    public async Task Browser_tier_is_routed_to_the_browser_acquirer()
    {
        var http = new RecordingAcquirer(Content());
        var browser = new RecordingAcquirer(Content());
        var acquirer = new TieredContentAcquirer(http, browser);

        await acquirer.AcquireAsync(Request(AcquisitionTier.Browser));

        Assert.Empty(http.Requests);
        Assert.Single(browser.Requests);
    }

    [Fact]
    public async Task A_failed_http_tier_request_never_escalates_to_browser()
    {
        var expected = new AcquisitionException("SNR-ACQ-999", "HTTP acquisition failed.");
        var http = new RecordingAcquirer(exception: expected);
        var browser = new RecordingAcquirer(Content());
        var acquirer = new TieredContentAcquirer(http, browser);

        var actual = await Assert.ThrowsAsync<AcquisitionException>(
            async () => await acquirer.AcquireAsync(Request(AcquisitionTier.Html)));

        Assert.Same(expected, actual);
        Assert.Single(http.Requests);
        Assert.Empty(browser.Requests);
    }

    private static AcquisitionRequest Request(AcquisitionTier tier) => new(Target, "example/products", Tier: tier);

    private static AcquiredContent Content() => new(
        Target,
        Target,
        200,
        "text/html",
        Encoding.UTF8,
        Encoding.UTF8.GetBytes("<html />"),
        new Dictionary<string, string>(),
        ContentOrigin.Network,
        null,
        TimeSpan.Zero);

    private sealed class RecordingAcquirer(AcquiredContent? content = null, Exception? exception = null) : IContentAcquirer
    {
        public List<AcquisitionRequest> Requests { get; } = [];

        public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (exception is not null)
            {
                throw exception;
            }

            return ValueTask.FromResult(content!);
        }
    }
}
