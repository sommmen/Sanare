using System.Text;
using Sanare.Abstractions;
using Sanare.Core.Acquisition;
using Sanare.Http.Resilience;

namespace Sanare.Http.Tests.Acquisition;

public sealed class AcquisitionPipelineFactoryTests
{
    [Fact]
    public async Task CreateBrowser_composes_the_browser_acquirer_with_shared_governance()
    {
        AcquisitionGovernance? capturedGovernance = null;
        var browser = new RecordingAcquirer(Content());
        using var client = new HttpClient(new ScriptedHandler());

        var pipeline = AcquisitionPipelineFactory.CreateBrowser(
            client,
            new RecordingFixtureCorpus(),
            governance =>
            {
                capturedGovernance = governance;
                return browser;
            });

        await pipeline.AcquireAsync(new AcquisitionRequest(Target, "example/products", Tier: AcquisitionTier.Browser));

        var governance = Assert.IsType<AcquisitionGovernance>(capturedGovernance);
        Assert.IsType<BlockCircuitBreaker>(governance.Breaker);
        Assert.NotNull(governance.Limiters);
        Assert.NotNull(governance.Robots);
        Assert.Same(TimeProvider.System, governance.Clock);
        Assert.Single(browser.Requests);
    }

    private static readonly Uri Target = new("https://example.test/products/1");

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

    private sealed class RecordingAcquirer(AcquiredContent content) : IContentAcquirer
    {
        public List<AcquisitionRequest> Requests { get; } = [];

        public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return ValueTask.FromResult(content);
        }
    }
}
