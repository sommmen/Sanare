using Sanare.Abstractions;
using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Attributes;
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
            diagnostic.Severity == DiagnosticSeverity.Info &&
            diagnostic.Message == "A fallback locator succeeded.");
    }

    [Fact]
    public void Execute_locates_a_value_with_an_xpath_expression()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.XPath, ["//div[@class='target']"]),
        ]);

        var outcome = Execute(plan, "<div class='other'>Nope</div><div class='target'>Yoga Tab</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal("Yoga Tab", outcome.Values["/Name"]);
    }

    [Fact]
    public void Execute_html_locator_yields_the_document_markup_for_a_downstream_regex_capture()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.Html, []),
            new LocatorStep(PlanOperation.RegexCapture, ["Yoga Tab \\d+"]),
        ]);

        var outcome = Execute(plan, "<div>prefix Yoga Tab 2 suffix</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal("Yoga Tab 2", outcome.Values["/Name"]);
    }

    [Fact]
    public void Execute_regex_capture_supports_an_explicit_group_index()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.RegexCapture, ["SKU: (\\w+)", "1"]),
        ]);

        var outcome = Execute(plan, "<div class='payload'>SKU: LEN103Y0003</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal("LEN103Y0003", outcome.Values["/Name"]);
    }

    [Theory]
    [InlineData("$.product.offers.price", "649.01")]
    [InlineData("$.product.images[0].url", "first")]
    [InlineData("$.product.images[*].url", "first")]
    public void Execute_json_path_locates_supported_nested_values(string path, string expected)
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.JsonPath, [path]),
        ]);

        var outcome = Execute(plan, "<script class='payload'>{\"product\":{\"offers\":{\"price\":649.01},\"images\":[{\"url\":\"first\"},{\"url\":\"second\"}]}}</script>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal(expected, outcome.Values["/Name"]);
    }

    [Theory]
    [InlineData("$.product.missing")]
    [InlineData("product.name")]
    public void Execute_json_path_miss_or_invalid_path_yields_null(string path)
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.JsonPath, [path]),
        ]);

        var outcome = Execute(plan, "<script class='payload'>{\"product\":{\"name\":\"Yoga Tab\"}}</script>");

        Assert.False(outcome.RequiredFieldsPresent);
        Assert.Null(outcome.Values["/Name"]);
    }

    [Fact]
    public void Execute_json_path_malformed_json_yields_null()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.JsonPath, ["$.product.name"]),
        ]);

        var outcome = Execute(plan, "<script class='payload'>{\"product\":</script>");

        Assert.False(outcome.RequiredFieldsPresent);
        Assert.Null(outcome.Values["/Name"]);
    }

    [Fact]
    public void Execute_regex_capture_miss_yields_null_rather_than_throwing()
    {
        var plan = CreatePlan(
        [
            new LocatorStep(PlanOperation.SelectFirst, [".payload"]),
            new LocatorStep(PlanOperation.RegexCapture, ["NoMatch\\d+"]),
        ]);

        var outcome = Execute(plan, "<div class='payload'>Yoga Tab</div>");

        Assert.False(outcome.RequiredFieldsPresent);
        Assert.Null(outcome.Values["/Name"]);
    }

    [Theory]
    [InlineData(PlanOperation.Split, "Yoga|Tab|2", new[] { "|" }, "Yoga Tab 2")]
    [InlineData(PlanOperation.Index, "Yoga\nTab\n2", new[] { "1" }, "Tab")]
    [InlineData(PlanOperation.Concat, "Yoga\nTab\n2", new[] { " " }, "Yoga Tab 2")]
    [InlineData(PlanOperation.Coalesce, "\n\nYoga\nTab", new string[0], "Yoga")]
    [InlineData(PlanOperation.Exists, "Yoga", new string[0], "true")]
    [InlineData(PlanOperation.MapEnum, "http://schema.org/InStock", new[] { "http://schema.org/InStock", "InStock" }, "InStock")]
    [InlineData(PlanOperation.ParseInt, "649", new string[0], "649")]
    [InlineData(PlanOperation.ParseDecimal, "649.01", new string[0], "649.01")]
    [InlineData(PlanOperation.ParseBool, "TRUE", new string[0], "true")]
    [InlineData(PlanOperation.Html, "<ul><li>Yoga</li><li>Tab</li></ul>", new string[0], "Yoga Tab")]
    public void Execute_applies_demo_transforms(PlanOperation operation, string value, string[] arguments, string expected)
    {
        var plan = CreatePlan(
            [new LocatorStep(PlanOperation.SelectFirst, [".payload"])],
            [new TransformStep(operation, arguments)]);

        var payload = operation == PlanOperation.Html ? System.Net.WebUtility.HtmlEncode(value) : value;
        var outcome = Execute(plan, $"<div class='payload'>{payload}</div>");

        Assert.True(outcome.RequiredFieldsPresent);
        Assert.Equal(expected, outcome.Values["/Name"]);
    }

    [Fact]
    public void ExecuteMany_extracts_html_items_and_preserves_failed_items()
    {
        var plan = CreatePlan([new LocatorStep(PlanOperation.Text, [".name"])]) with
        {
            Root = ".item",
            Fields = [new FieldPlan("/Products/*/Name", true, "string", [new LocatorStep(PlanOperation.Text, [".name"])], [])],
        };
        var schema = new SchemaDeriver().Derive<ProductCollection>();

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, "<div class='item'><span class='name'>Yoga</span></div><div class='item'></div>", schema);

        Assert.Equal(2, outcomes.Length);
        Assert.Equal("Yoga", outcomes[0].Values["/Name"]);
        Assert.False(outcomes[1].RequiredFieldsPresent);
    }

    [Fact]
    public void ExecuteMany_extracts_json_items_and_honours_max_items()
    {
        var plan = CreatePlan([new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]), new LocatorStep(PlanOperation.JsonPath, ["$.name"])]) with
        {
            Root = "$.items",
            Pagination = new PaginationSpec(PaginationStrategy.None, MaxItems: 1),
            Fields = [new FieldPlan("/Products/*/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]), new LocatorStep(PlanOperation.JsonPath, ["$.name"])], [])],
        };
        var schema = new SchemaDeriver().Derive<ProductCollection>();

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, "{\"items\":[{\"name\":\"Yoga\"},{\"name\":\"Tab\"}]}", schema);

        var outcome = Assert.Single(outcomes);
        Assert.Equal("Yoga", outcome.Values["/Name"]);
    }

    [Fact]
    public void ExecuteMany_excludes_top_level_fields_that_collide_with_item_fields()
    {
        var locator = new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]);
        var plan = CreatePlan([locator]) with
        {
            Root = "$.products",
            Fields =
            [
                new FieldPlan("/Name", true, "string", [locator, new LocatorStep(PlanOperation.JsonPath, ["$.name"])], []),
                new FieldPlan("/Products/*/Name", true, "string", [locator, new LocatorStep(PlanOperation.JsonPath, ["$.name"])], []),
            ],
        };

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, "{\"name\":\"page\",\"products\":[{\"name\":\"Yoga\"}]}", new SchemaDeriver().Derive<ProductPage>());

        var outcome = Assert.Single(outcomes);
        Assert.Equal("Yoga", outcome.Values["/Name"]);
    }

    [Fact]
    public void ExecuteMany_extracts_items_from_doubly_nested_json_wildcards()
    {
        var locator = new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]);
        var plan = CreatePlan([locator]) with
        {
            Root = "$.groups[*].products[*]",
            Fields = [new FieldPlan("/Products/*/Name", true, "string", [locator, new LocatorStep(PlanOperation.JsonPath, ["$.name"])], [])],
        };

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, "{\"groups\":[{\"products\":[{\"name\":\"Yoga\"}]},{\"products\":[{\"name\":\"Tab\"}]}]}", new SchemaDeriver().Derive<ProductCollection>());

        Assert.Equal(["Yoga", "Tab"], outcomes.Select(outcome => outcome.Values["/Name"]));
    }

    [Fact]
    public void ExecuteMany_finds_a_json_island_embedded_in_markup_without_site_specific_knowledge()
    {
        var locator = new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]);
        var plan = CreatePlan([locator]) with
        {
            Root = "$.groups[*].products[*]",
            Fields = [new FieldPlan("/Products/*/Name", true, "string", [locator, new LocatorStep(PlanOperation.JsonPath, ["$.name"])], [])],
        };

        // The island is named by an arbitrary variable, and an unrelated script precedes it. Neither the
        // variable name nor the page's structure may be known to this generic runtime.
        const string markup = """
            <html><body>
            <script>window.__ANALYTICS__ = {"session":"abc"};</script>
            <script>var $someOtherSitesPayload = {"groups":[{"products":[{"name":"Yoga"}]},{"products":[{"name":"Tab"}]}]};</script>
            </body></html>
            """;

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, markup, new SchemaDeriver().Derive<ProductCollection>());

        Assert.Equal(["Yoga", "Tab"], outcomes.Select(outcome => outcome.Values["/Name"]));
    }

    [Fact]
    public void ExecuteMany_ignores_a_json_island_whose_braces_appear_inside_strings()
    {
        var locator = new LocatorStep(PlanOperation.SelectFirst, [".sanare-json-item"]);
        var plan = CreatePlan([locator]) with
        {
            Root = "$.groups[*].products[*]",
            Fields = [new FieldPlan("/Products/*/Name", true, "string", [locator, new LocatorStep(PlanOperation.JsonPath, ["$.name"])], [])],
        };

        // A brace inside a string literal must not terminate the island early, or the payload that
        // follows it is lost.
        const string markup = """
            <html><body>
            <script>var payload = {"label":"a } brace","groups":[{"products":[{"name":"Yoga"}]}]};</script>
            </body></html>
            """;

        var outcomes = new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, markup, new SchemaDeriver().Derive<ProductCollection>());

        Assert.Equal(["Yoga"], outcomes.Select(outcome => outcome.Values["/Name"]));
    }

    [Fact]
    public void ExecuteMany_returns_no_outcomes_for_an_empty_collection()
    {
        var plan = CreatePlan([new LocatorStep(PlanOperation.Text, [".name"])]) with { Root = ".item" };
        var schema = new SchemaDeriver().Derive<ProductCollection>();

        Assert.Empty(new PlanExecutor(new TypeCoercer()).ExecuteMany(plan, "<div></div>", schema));
    }

    private static ExtractionOutcome Execute(ExtractionPlan plan, string html)
    {
        var schema = new SchemaDeriver().Derive<Product>();
        return new PlanExecutor(new TypeCoercer()).Execute(plan, html, schema);
    }

    private static ExtractionPlan CreatePlan(IReadOnlyList<LocatorStep> locators, IReadOnlyList<TransformStep>? transforms = null) => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
        SourceId = "example/products",
        SchemaName = nameof(Product),
        SchemaVersion = 1,
        SchemaHash = string.Empty,
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, StartUrl.AbsoluteUri, new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        Fields = [new FieldPlan("/Name", true, "string", locators, transforms ?? Array.Empty<TransformStep>())],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 1d, DateTimeOffset.UnixEpoch),
    };

    [Fact]
    public void Execute_reads_a_localised_value_using_the_fields_own_culture()
    {
        // No parser transform runs, so the raw page text is locale-formatted and must be read as nl-NL:
        // "649,01" is six hundred forty-nine and one cent, not sixty-four thousand nine hundred and one.
        var plan = CreatePlan([new LocatorStep(PlanOperation.Text, [".price"])]) with
        {
            Culture = "nl-NL",
            Fields = [new FieldPlan("/Price", false, "decimal", [new LocatorStep(PlanOperation.Text, [".price"])], [])],
        };

        var schema = new SchemaDeriver().Derive<PricedProduct>("nl-NL");
        Assert.Equal("nl-NL", schema.Fields.Single(field => field.JsonPointer == "/Price").Culture);

        var outcome = new PlanExecutor(new TypeCoercer()).Execute(plan, "<span class='price'>649,01</span>", schema);

        Assert.Equal(649.01m, outcome.Values["/Price"]);
    }

    [Fact]
    public void Execute_does_not_reapply_the_field_culture_to_a_parsed_value()
    {
        // ParseDecimal emits its result in the invariant culture. Reparsing that canonical "649.01"
        // under nl-NL, where '.' groups thousands, would silently yield 64901.
        var plan = CreatePlan([new LocatorStep(PlanOperation.Text, [".price"])]) with
        {
            Culture = "nl-NL",
            Fields =
            [
                new FieldPlan("/Price", false, "decimal",
                    [new LocatorStep(PlanOperation.Text, [".price"])],
                    [new TransformStep(PlanOperation.ParseDecimal, ["en-US"])]),
            ],
        };

        var outcome = new PlanExecutor(new TypeCoercer())
            .Execute(plan, "<span class='price'>649.01</span>", new SchemaDeriver().Derive<PricedProduct>("nl-NL"));

        Assert.Equal(649.01m, outcome.Values["/Price"]);
    }

    [Fact]
    public void Execute_reports_a_malformed_url_template_as_a_diagnostic_rather_than_throwing()
    {
        var plan = CreatePlan([new LocatorStep(PlanOperation.Text, [".name"])]) with
        {
            Acquisition = new AcquisitionSpec(
                AcquisitionMethod.Get, "not-an-absolute-uri", new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        };

        var outcome = new PlanExecutor(new TypeCoercer())
            .Execute(plan, "<span class='name'>Yoga</span>", new SchemaDeriver().Derive<Product>());

        Assert.Equal("Yoga", outcome.Values["/Name"]);
        Assert.Contains(outcome.Diagnostics, diagnostic => diagnostic.JsonPointer == "/acquisition/urlTemplate");
    }

    private sealed class Product
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class PricedProduct
    {
        public decimal? Price { get; set; }
    }

    private sealed class ProductCollection
    {
        [ScrapeCollection]
        public List<Product> Products { get; set; } = [];
    }

    private sealed class ProductPage
    {
        public string Name { get; set; } = string.Empty;

        [ScrapeCollection]
        public List<Product> Products { get; set; } = [];
    }
}
