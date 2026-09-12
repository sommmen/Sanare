using System.Globalization;
using System.Text;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core;
using Sanare.Core.Acquisition;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;
using Sanare.Core.Schema.Materialization;
using Sanare.Http.Identity;
using Sanare.Http.Identity.Profiles;

namespace Sanare.Http.Tests;

public sealed class AcquisitionScrapeRunnerTests
{
    private const string SourceId = "lenovo/tablets";
    private static readonly Uri ProductUrl = new("https://www.lenovo.example/tablets/yoga-tab");
    private static readonly IReadOnlySet<string> SupportedEncodings = new HashSet<string>(StringComparer.Ordinal) { "gzip", "deflate", "br" };

    [Fact]
    public async Task RunAsync_executes_acquired_content_with_the_selected_identity()
    {
        var acquirer = new RecordingContentAcquirer([Content("<html><h1 class='name'>Yoga Tab</h1><span class='price'>$499.99</span></html>")]);
        var runner = CreateRunner(PlanFor<Product>(), acquirer);

        var result = await runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.Succeeded, result.Status);
        var payload = Assert.IsType<Product>(result.Payload);
        Assert.Equal("Yoga Tab", payload.Name);
        Assert.Equal(499.99m, payload.Price);
        Assert.Equal(1, result.Provenance.RequestsIssued);
        Assert.Equal(ResultOrigin.Network, result.Provenance.Origin);
        Assert.Equal("AssistantBrowser", result.Provenance.IdentityProfileId);
        var issuedRequest = Assert.Single(acquirer.Requests);
        Assert.Equal("AssistantBrowser", issuedRequest.Identity?.ProfileId);
    }

    [Fact]
    public async Task RunAsync_retries_once_with_the_consent_cookie_when_a_wall_is_detected()
    {
        var acquirer = new RecordingContentAcquirer(
        [
            Content(LoadFixture("consent-wall-onetrust.html")),
            Content("<html><main id='product-detail'><h1 class='name'>Lenovo Tab P11</h1><span class='price'>€249.00</span></main></html>"),
        ]);
        var runner = CreateRunner(PlanFor<Product>(), acquirer);

        var result = await runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.Succeeded, result.Status);
        Assert.Equal(2, result.Provenance.RequestsIssued);
        Assert.Equal(2, acquirer.Requests.Count);
        Assert.DoesNotContain(acquirer.Requests[0].Identity!.Cookies, cookie => cookie.Name == "OptanonAlertBoxClosed");
        Assert.Contains(acquirer.Requests[1].Identity!.Cookies, cookie => cookie.Name == "OptanonAlertBoxClosed" && cookie.Value == "1");
    }

    [Fact]
    public async Task RunAsync_stops_after_one_retry_when_the_consent_wall_persists()
    {
        var acquirer = new RecordingContentAcquirer(
        [
            Content(LoadFixture("consent-wall-onetrust.html")),
            Content(LoadFixture("consent-wall-onetrust.html")),
        ]);
        var runner = CreateRunner(PlanFor<Product>(), acquirer);

        var result = await runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.ConsentWallBlocked, result.Status);
        Assert.Null(result.Payload);
        Assert.Equal(2, result.Provenance.RequestsIssued);
        Assert.Equal(2, acquirer.Requests.Count);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-ACQ-005");
    }

    [Fact]
    public async Task RunAsync_maps_acquisition_failures_to_a_result_status()
    {
        var runner = CreateRunner(PlanFor<Product>(), new RecordingContentAcquirer([], new AcquisitionException("SNR-ACQ-007", "Response exceeded maximum size.")));

        var result = await runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.ExtractionFailed, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-ACQ-007");
    }

    [Fact]
    public async Task RunAsync_returns_no_plan_available_when_no_plan_is_registered()
    {
        var plans = new InMemoryExtractionPlanProvider(new Dictionary<string, ExtractionPlan>());
        var runner = CreateRunner(plans, new RecordingContentAcquirer([Content("<html></html>")]));

        var result = await runner.Runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.NoPlanAvailable, result.Status);
        Assert.Null(result.Payload);
        Assert.Empty(runner.AcquirerRequests);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-PLAN-001");
    }

    [Fact]
    public async Task RunAsync_returns_plan_invalid_for_an_unsupported_plan_operation()
    {
        var invalidPlan = PlanFor<Product>() with
        {
            Fields =
            [
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, [])]),
                new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.XPath, ["//span"])], []),
            ],
        };
        var runner = CreateRunner(invalidPlan, new RecordingContentAcquirer([Content("<html><h1 class='name'>Yoga Tab</h1><span>$499.99</span></html>")]));

        var result = await runner.RunAsync<Product>(Request());

        Assert.Equal(ScrapeStatus.PlanInvalid, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-PLAN-002");
    }

    [Fact]
    public async Task StreamAsync_throws_not_supported()
    {
        var runner = CreateRunner(PlanFor<Product>(), new RecordingContentAcquirer([Content("<html></html>")]));

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in runner.StreamAsync<Product>(Request()))
            {
            }
        });
    }

    private static ScrapeRequest Request() => new() { Url = ProductUrl, SourceId = SourceId };

    private static AcquisitionScrapeRunner CreateRunner(ExtractionPlan plan, RecordingContentAcquirer acquirer) =>
        CreateRunner(new InMemoryExtractionPlanProvider(new Dictionary<string, ExtractionPlan>
        {
            [InMemoryExtractionPlanProvider.Key(SourceId, typeof(Product))] = plan,
        }), acquirer).Runner;

    private static (AcquisitionScrapeRunner Runner, IReadOnlyList<AcquisitionRequest> AcquirerRequests) CreateRunner(
        InMemoryExtractionPlanProvider plans,
        RecordingContentAcquirer acquirer)
    {
        var options = new IdentityOptions(
            Profiles: [new AssistantBrowserProfile()],
            DefaultProfileId: "AssistantBrowser",
            SupportedContentEncodings: SupportedEncodings,
            SourceOverrides: new Dictionary<string, SourceIdentityOverride>
            {
                [SourceId] = new(ExpectedContentRootSelector: "#product-detail"),
            });
        return (
            new AcquisitionScrapeRunner(
                plans,
                acquirer,
                new BrowsingIdentityProvider(options),
                new SchemaDeriver(),
                new PlanExecutor(new TypeCoercer()),
                new SchemaValidator(),
                new DocumentMaterializer()),
            acquirer.Requests);
    }

    private static ExtractionPlan PlanFor<TSchema>() where TSchema : class => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
        SourceId = SourceId,
        SchemaName = typeof(TSchema).Name,
        SchemaVersion = 1,
        SchemaHash = string.Empty,
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, ProductUrl.AbsoluteUri, new Dictionary<string, string>(), null, []),
        Fields =
        [
            new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, [])]),
            new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.SelectFirst, [".price"])], [new TransformStep(PlanOperation.StripCurrency, []), new TransformStep(PlanOperation.Trim, [])]),
        ],
        Provenance = new PlanProvenance("test", "none", 1, [], 1d, DateTimeOffset.UnixEpoch),
    };

    private static AcquiredContent Content(string html) => new(
        ProductUrl,
        ProductUrl,
        200,
        "text/html",
        Encoding.UTF8,
        Encoding.UTF8.GetBytes(html),
        new Dictionary<string, string>(),
        ContentOrigin.Network,
        null,
        TimeSpan.Zero);

    private static string LoadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", fileName));

    private sealed class RecordingContentAcquirer(IReadOnlyList<AcquiredContent> responses, Exception? exception = null) : IContentAcquirer
    {
        private readonly Queue<AcquiredContent> _responses = new(responses);

        public List<AcquisitionRequest> Requests { get; } = [];

        public ValueTask<AcquiredContent> AcquireAsync(AcquisitionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (exception is not null)
            {
                throw exception;
            }

            return ValueTask.FromResult(_responses.Dequeue());
        }
    }

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }
}
