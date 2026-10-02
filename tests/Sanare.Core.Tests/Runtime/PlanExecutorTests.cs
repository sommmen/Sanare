using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;

namespace Sanare.Core.Tests.Runtime;

public sealed class PlanExecutorTests
{
    private static readonly Uri StartUrl = new("https://example.test/product");

    [Fact]
    public void Execute_pipes_a_value_kind_locator_from_the_preceding_step()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.Text, [".value"]),
        ]);

        var outcome = Execute(plan, "<div class='payload'>&lt;span class='value'&gt;Yoga Tab&lt;/span&gt;</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal("Yoga Tab", outcome.Values["/Name"]);
        Assert.Empty(outcome.Diagnostics);
    }

    [Fact]
    public void Execute_restarts_from_the_document_for_a_repeated_document_locator()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".missing"]),
            new LocatorStep(PlanOperation.SelectFirst, [".fallback"]),
        ]);

        var outcome = Execute(plan, "<div class='fallback'>Yoga Tab</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal("Yoga Tab", outcome.Values["/Name"]);
        Assert.Contains(outcome.Diagnostics, diagnostic =>
            diagnostic.Severity == Sanare.Abstractions.Diagnostics.DiagnosticSeverity.Info &&
            diagnostic.Message == "A fallback locator succeeded.");
    }

    private static ExtractionOutcome Execute(ExtractionPlan plan, string html)
    {
        var schema = new SchemaDeriver().Derive<Product>();
        return new PlanExecutor(new TypeCoercer()).Execute(plan, html, schema);
    }

    private static ExtractionPlan CreatePlan(IReadOnlyList<LocatorStep> locators) => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
        SourceId = "example/products",
        SchemaName = nameof(Product),
        SchemaVersion = 1,
        SchemaHash = string.Empty,
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, StartUrl.AbsoluteUri, new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        Fields = [new FieldPlan("/Name", true, "string", locators, Array.Empty<TransformStep>())],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 1d, DateTimeOffset.UnixEpoch),
    };

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;
    }
}
