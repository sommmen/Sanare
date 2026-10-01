using Sanare.Abstractions;
using Sanare.Core.Acquisition;
using Sanare.Http;

namespace Sanare.Browser.Tests;

[Trait("Category", "Browser")]
public sealed class TieredContentAcquirerIntegrationTests
{
    [Fact]
    public async Task Http_failure_does_not_escalate_to_the_browser_acquirer()
    {
        var http = new ThrowingAcquirer();
        var browser = new RecordingAcquirer();
        var acquirer = new TieredContentAcquirer(http, browser);
        var request = new AcquisitionRequest(new Uri("https://example.test/"), "browser-test", Tier: AcquisitionTier.Html);

        var exception = await Assert.ThrowsAsync<AcquisitionException>(async () => await acquirer.AcquireAsync(request));

        Assert.Equal("SNR-HTTP-TEST", exception.Code);
        Assert.Equal(0, browser.CallCount);
    }

    private sealed class ThrowingAcquirer : IContentAcquirer
    {
        public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default) =>
            ValueTask.FromException<AcquiredContent>(new AcquisitionException("SNR-HTTP-TEST", "HTTP failed."));
    }

    private sealed class RecordingAcquirer : IContentAcquirer
    {
        public int CallCount { get; private set; }

        public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
        {
            CallCount++;
            throw new InvalidOperationException("Browser acquisition must not be called.");
        }
    }
}
