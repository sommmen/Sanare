using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Schema;
using Xunit;

namespace Sanare.Core.Tests.Plans;

public sealed class PlanValidatorTests
{
    private static readonly Uri ProductUrl = new("https://example.test/products/1");

    [Fact]
    public void Validate_returns_no_defects_for_a_well_formed_plan()
    {
        var result = new PlanValidator().Validate(SamplePlan());

        Assert.True(result.IsValid);
        Assert.Empty(result.Defects);
    }

    [Fact]
    public void Validate_fails_when_an_operation_has_too_few_arguments()
    {
        // AC-PLAN-008: SelectFirst requires exactly one argument.
        var plan = SamplePlan() with
        {
            Fields =
            [
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [])], []),
            ],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/locators/0");
    }

    [Fact]
    public void Validate_fails_when_an_html_tier_plan_uses_a_browser_only_interaction()
    {
        // AC-PLAN-006
        var plan = SamplePlan() with
        {
            Tier = AcquisitionTier.Html,
            Acquisition = SamplePlan().Acquisition with
            {
                Interactions = [new InteractionStep(PlanOperation.Click, [".accept"])],
            },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/acquisition/interactions/0");
    }

    [Fact]
    public void Validate_succeeds_when_a_browser_tier_plan_uses_a_browser_only_interaction()
    {
        // AC-PLAN-007
        var plan = SamplePlan() with
        {
            Tier = AcquisitionTier.Browser,
            Acquisition = SamplePlan().Acquisition with
            {
                Interactions = [new InteractionStep(PlanOperation.Click, [".accept"])],
            },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_a_required_schema_pointer_is_not_covered()
    {
        // AC-PLAN-009
        var schema = SampleSchema();
        var plan = SamplePlan() with
        {
            Fields = [new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [])],
        };

        var result = new PlanValidator().Validate(plan, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/Price");
    }

    [Fact]
    public void Validate_fails_when_two_fields_share_the_same_pointer()
    {
        // AC-PLAN-010
        var plan = SamplePlan() with
        {
            Fields =
            [
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".a"])], []),
                new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".b"])], []),
            ],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/1/pointer");
    }

    [Fact]
    public void Validate_fails_when_headers_contain_a_cookie_key()
    {
        // AC-PLAN-011
        var plan = SamplePlan() with
        {
            Acquisition = SamplePlan().Acquisition with { Headers = new Dictionary<string, string> { ["Cookie"] = "a=b" } },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/acquisition/headers/Cookie");
    }

    [Theory]
    [InlineData(10_001, false)]
    [InlineData(10_000, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void Validate_checks_MaxPages_boundaries(int maxPages, bool expectedValid)
    {
        // AC-PLAN-012
        var plan = SamplePlan() with { Pagination = new PaginationSpec(PaginationStrategy.PageNumber, MaxPages: maxPages) };

        var result = new PlanValidator().Validate(plan);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Validate_reports_every_defect_present_in_one_result()
    {
        // AC-PLAN-014: four distinct, independent defects reported together.
        var plan = SamplePlan() with
        {
            SchemaHash = string.Empty,
            Consent = new ConsentSpec("unknown-strategy"),
            Pagination = new PaginationSpec(PaginationStrategy.PageNumber, MaxPages: 10_001),
            Acquisition = SamplePlan().Acquisition with { Headers = new Dictionary<string, string> { ["Authorization"] = "x" } },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Equal(4, result.Defects.Count);
    }

    [Fact]
    public void Validate_fails_when_a_json_api_tier_plan_uses_xpath()
    {
        // AC-PLAN-015
        var plan = SamplePlan() with
        {
            Tier = AcquisitionTier.JsonApi,
            Fields = [new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.XPath, ["//name"])], [])],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/locators/0");
    }

    [Fact]
    public void Validate_fails_when_embedded_schema_hash_disagrees_with_the_supplied_schema()
    {
        // AC-PLAN-017
        var schema = SampleSchema();
        var plan = SamplePlan() with { SchemaHash = "does-not-match" };

        var result = new PlanValidator().Validate(plan, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/schemaHash" && d.Code == "SNR-PLAN-001");
    }

    [Fact]
    public void Validate_succeeds_when_embedded_schema_hash_agrees_with_the_supplied_schema()
    {
        var schema = SampleSchema();
        var plan = SamplePlan() with { SchemaHash = schema.Hash };

        var result = new PlanValidator().Validate(plan, schema);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_for_an_unknown_consent_strategy()
    {
        // AC-023
        var plan = SamplePlan() with { Consent = new ConsentSpec("basic-auth") };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/consent/strategy" && d.Code == "SNR-PLAN-001");
    }

    [Fact]
    public void Validate_succeeds_for_the_cookie_consent_strategy()
    {
        var plan = SamplePlan() with { Consent = new ConsentSpec("cookie", "OptanonAlertBoxClosed") };

        var result = new PlanValidator().Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_a_field_has_no_locators()
    {
        var plan = SamplePlan() with
        {
            Fields = [new FieldPlan("/Name", true, "string", [], [])],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/locators");
    }

    [Fact]
    public void Validate_fails_for_a_malformed_field_pointer()
    {
        var plan = SamplePlan() with
        {
            Fields = [new FieldPlan("Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [])],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/pointer");
    }

    [Fact]
    public void Validate_fails_for_a_relative_url_template()
    {
        var plan = SamplePlan() with
        {
            Acquisition = SamplePlan().Acquisition with { UrlTemplate = "/products/1" },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/acquisition/urlTemplate");
    }

    [Fact]
    public void Validate_succeeds_for_an_absolute_url_template_containing_placeholders()
    {
        var plan = SamplePlan() with
        {
            Acquisition = SamplePlan().Acquisition with { UrlTemplate = "https://example.test/products?page={page}" },
        };

        var result = new PlanValidator().Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_when_a_load_more_button_strategy_targets_a_non_browser_tier()
    {
        var plan = SamplePlan() with
        {
            Tier = AcquisitionTier.Html,
            Pagination = new PaginationSpec(PaginationStrategy.LoadMoreButton),
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/pagination/strategy");
    }

    [Fact]
    public void Validate_succeeds_when_a_load_more_button_strategy_targets_the_browser_tier()
    {
        var plan = SamplePlan() with
        {
            Tier = AcquisitionTier.Browser,
            Pagination = new PaginationSpec(PaginationStrategy.LoadMoreButton),
        };

        var result = new PlanValidator().Validate(plan);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_fails_for_an_empty_schema_hash()
    {
        var plan = SamplePlan() with { SchemaHash = string.Empty };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/schemaHash");
    }

    [Fact]
    public void Validate_fails_when_a_pattern_argument_does_not_compile_as_a_regex()
    {
        var plan = SamplePlan() with
        {
            Fields = [new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.RegexCapture, ["("])], [])],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/locators/0/arguments/0");
    }

    [Fact]
    public void Validate_fails_when_an_int_argument_does_not_parse()
    {
        var plan = SamplePlan() with
        {
            Fields = [new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.RegexCapture, [".", "not-an-int"])], [])],
        };

        var result = new PlanValidator().Validate(plan);

        Assert.False(result.IsValid);
        Assert.Contains(result.Defects, d => d.PlanPointer == "/fields/0/locators/0/arguments/1");
    }

    private static SchemaDescriptor SampleSchema() => new(
        typeof(object),
        "Product",
        1,
        "{}",
        "abc123",
        [
            new FieldDescriptor("/Name", "Name", typeof(string), true, null, null, null, null),
            new FieldDescriptor("/Price", "Price", typeof(decimal), true, null, null, null, null),
        ]);

    private static ExtractionPlan SamplePlan() => new()
    {
        PlanVersion = 1,
        SourceId = "lenovo/tablets",
        SchemaName = "Product",
        SchemaVersion = 1,
        SchemaHash = "abc123",
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, ProductUrl.AbsoluteUri, new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        Fields =
        [
            new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
            new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.SelectFirst, [".price"])], [new TransformStep(PlanOperation.StripCurrency, Array.Empty<string>()), new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
        ],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 0.987654d, DateTimeOffset.UnixEpoch),
    };
}
