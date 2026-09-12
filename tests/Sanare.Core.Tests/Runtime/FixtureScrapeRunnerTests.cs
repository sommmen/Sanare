using Sanare.Abstractions;
using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Plans;
using Sanare.Core.Fixtures;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;
using Sanare.Core.Schema.Materialization;

namespace Sanare.Core.Tests.Runtime;

public sealed class FixtureScrapeRunnerTests
{
    private static readonly Uri ProductUrl = new("https://www.lenovo.example/tablets/yoga-tab");

    [Fact]
    public async Task RunAsync_executes_fixture_plan_end_to_end()
    {
        var runner = CreateRunner(PlanFor<Product>());

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" });

        Assert.Equal(ScrapeStatus.Succeeded, result.Status);
        Assert.NotNull(result.Payload);
        Assert.Equal("Yoga Tab", result.Payload.Name);
        Assert.Equal(499.99m, result.Payload.Price);
        Assert.True(result.Provenance.ServedFromFixture);
    }

    [Fact]
    public async Task RunAsync_returns_schema_error_for_missing_required_field()
    {
        var missing = new Uri("https://www.lenovo.example/tablets/missing");
        var runnerWithMissingPrice = CreateRunner(PlanFor<Product>(), missing, "<html><h1 class='name'>Yoga Tab</h1></html>");

        var result = await runnerWithMissingPrice.RunAsync<Product>(new ScrapeRequest { Url = missing, SourceId = "lenovo/tablets" });

        Assert.Equal(ScrapeStatus.SchemaValidationFailed, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-SCH-004");
    }

    [Fact]
    public async Task RunAsync_returns_invalid_request_for_malformed_url()
    {
        var runner = CreateRunner(PlanFor<Product>());
        var relativeUri = new Uri("/tablets/yoga-tab", UriKind.Relative);

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = relativeUri, SourceId = "lenovo/tablets" });

        Assert.Equal(ScrapeStatus.InvalidRequest, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-001");
    }

    [Fact]
    public async Task RunAsync_returns_invalid_request_for_an_unresolvable_culture()
    {
        var runner = CreateRunner(PlanFor<Product>());

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets", Culture = "not-a-culture!" });

        Assert.Equal(ScrapeStatus.InvalidRequest, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-005");
    }

    [Fact]
    public async Task RunAsync_keeps_success_status_for_request_normalization_warnings()
    {
        var runner = CreateRunner(PlanFor<Product>());

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets", MaxItems = 0 });

        Assert.Equal(ScrapeStatus.Succeeded, result.Status);
        Assert.NotNull(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-API-003");
    }

    [Fact]
    public async Task RunAsync_distinguishes_optional_coercion_failure_from_a_missing_field()
    {
        var plan = PlanFor<ProductWithOptionalQuantity>() with
        {
            Fields =
            [
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
                new FieldPlan("/Quantity", false, "int", [new LocatorStep(PlanOperation.SelectFirst, [".quantity"])], Array.Empty<TransformStep>()),
            ],
        };
        var runner = CreateRunner<ProductWithOptionalQuantity>(plan, fixtureHtml: "<html><h1 class='name'>Yoga Tab</h1><span class='quantity'>many</span></html>");

        var result = await runner.RunAsync<ProductWithOptionalQuantity>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" });

        var quantity = Assert.Single(result.Quality.Fields, field => field.JsonPointer == "/Quantity");
        Assert.Equal(ScrapeStatus.SchemaValidationFailed, result.Status);
        Assert.Null(result.Payload);
        Assert.False(quantity.Present);
        Assert.False(quantity.CoercionSucceeded);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-SCH-005" && diagnostic.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RunAsync_returns_no_plan_available_when_no_plan_is_registered()
    {
        var plans = new InMemoryExtractionPlanProvider(new Dictionary<string, ExtractionPlan>());
        var fixtures = new InMemoryFixtureContentProvider(new Dictionary<string, string>
        {
            [InMemoryFixtureContentProvider.Key("lenovo/tablets", ProductUrl)] = "<html></html>",
        });
        var runner = new FixtureScrapeRunner(plans, fixtures, new SchemaDeriver(), new PlanExecutor(new TypeCoercer()), new SchemaValidator(), new DocumentMaterializer());

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" });

        Assert.Equal(ScrapeStatus.NoPlanAvailable, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-PLAN-001");
    }

    [Fact]
    public async Task RunAsync_returns_fixture_not_found_when_no_fixture_is_registered()
    {
        var sourceId = "lenovo/tablets";
        var plans = new InMemoryExtractionPlanProvider(new Dictionary<string, ExtractionPlan>
        {
            [InMemoryExtractionPlanProvider.Key(sourceId, typeof(Product))] = PlanFor<Product>(),
        });
        var fixtures = new InMemoryFixtureContentProvider(new Dictionary<string, string>());
        var runner = new FixtureScrapeRunner(plans, fixtures, new SchemaDeriver(), new PlanExecutor(new TypeCoercer()), new SchemaValidator(), new DocumentMaterializer());

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = sourceId });

        Assert.Equal(ScrapeStatus.FixtureNotFound, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-FIX-001");
    }

    [Fact]
    public async Task RunAsync_returns_plan_invalid_for_unsupported_operation()
    {
        var plan = PlanFor<Product>() with
        {
            Fields =
            [
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
                new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.XPath, ["//span"])], Array.Empty<TransformStep>()),
            ],
        };
        var runner = CreateRunner(plan);

        var result = await runner.RunAsync<Product>(new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" });

        Assert.Equal(ScrapeStatus.PlanInvalid, result.Status);
        Assert.Null(result.Payload);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "SNR-PLAN-002");
    }

    [Fact]
    public async Task RunAsync_is_deterministic_across_repeated_runs()
    {
        var runner = CreateRunner(PlanFor<Product>());
        var request = new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" };

        var first = await runner.RunAsync<Product>(request);
        var second = await runner.RunAsync<Product>(request);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Payload?.Name, second.Payload?.Name);
        Assert.Equal(first.Payload?.Price, second.Payload?.Price);
        Assert.Equal(first.Provenance.SchemaHash, second.Provenance.SchemaHash);
    }

    [Fact]
    public async Task StreamAsync_throws_not_supported()
    {
        var runner = CreateRunner(PlanFor<Product>());
        var request = new ScrapeRequest { Url = ProductUrl, SourceId = "lenovo/tablets" };

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (var _ in runner.StreamAsync<Product>(request))
            {
            }
        });
    }

    private static FixtureScrapeRunner CreateRunner(ExtractionPlan plan, Uri? fixtureUrl = null, string? fixtureHtml = null) =>
        CreateRunner<Product>(plan, fixtureUrl, fixtureHtml);

    private static FixtureScrapeRunner CreateRunner<TSchema>(ExtractionPlan plan, Uri? fixtureUrl = null, string? fixtureHtml = null)
        where TSchema : class
    {
        var sourceId = "lenovo/tablets";
        fixtureUrl ??= ProductUrl;
        fixtureHtml ??= "<html><h1 class='name'>Yoga Tab</h1><span class='price'> $499.99 </span></html>";
        var plans = new InMemoryExtractionPlanProvider(new Dictionary<string, ExtractionPlan>
        {
            [InMemoryExtractionPlanProvider.Key(sourceId, typeof(TSchema))] = plan,
        });
        var fixtures = new InMemoryFixtureContentProvider(new Dictionary<string, string>
        {
            [InMemoryFixtureContentProvider.Key(sourceId, fixtureUrl)] = fixtureHtml,
        });
        return new FixtureScrapeRunner(plans, fixtures, new SchemaDeriver(), new PlanExecutor(new TypeCoercer()), new SchemaValidator(), new DocumentMaterializer());
    }

    private static ExtractionPlan PlanFor<TSchema>() where TSchema : class => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
        SourceId = "lenovo/tablets",
        SchemaName = typeof(TSchema).Name,
        SchemaVersion = 1,
        SchemaHash = string.Empty,
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, ProductUrl.AbsoluteUri, new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        Fields =
        [
            new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
            new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.SelectFirst, [".price"])], [new TransformStep(PlanOperation.StripCurrency, Array.Empty<string>()), new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
        ],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 1d, DateTimeOffset.UnixEpoch),
    };

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;

        public decimal Price { get; set; }
    }

    private sealed class ProductWithOptionalQuantity
    {
        public string Name { get; set; } = string.Empty;

        public int? Quantity { get; set; }
    }
}
