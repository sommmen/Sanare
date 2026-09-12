using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Xunit;

namespace Sanare.Core.Tests.Plans;

public sealed class PlanSerializerTests
{
    private static readonly Uri ProductUrl = new("https://example.test/products/1");

    [Fact]
    public void WriteCanonical_round_trips_through_Read()
    {
        var serializer = new PlanSerializer();
        var plan = SamplePlan();

        var json = serializer.WriteCanonical(plan);
        var roundTripped = serializer.Read(json);

        Assert.Equal(json, serializer.WriteCanonical(roundTripped));
    }

    [Fact]
    public void WriteCanonical_is_idempotent()
    {
        var serializer = new PlanSerializer();
        var plan = SamplePlan();

        var first = serializer.WriteCanonical(plan);
        var second = serializer.WriteCanonical(serializer.Read(first));

        Assert.Equal(first, second);
    }

    [Fact]
    public void WriteCanonical_uses_LF_and_trailing_newline()
    {
        var serializer = new PlanSerializer();
        var json = serializer.WriteCanonical(SamplePlan());

        Assert.DoesNotContain("\r\n", json, StringComparison.Ordinal);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCanonical_orders_top_level_keys_per_tech_design()
    {
        var serializer = new PlanSerializer();
        var json = serializer.WriteCanonical(SamplePlan());

        var expectedOrder = new[]
        {
            "planVersion", "sourceId", "schemaName", "schemaVersion", "schemaHash",
            "culture", "tier", "acquisition", "notFound", "consent", "pagination",
            "root", "fields", "provenance",
        };

        var indices = expectedOrder
            .Select(key => json.IndexOf($"\"{key}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.All(indices, index => Assert.True(index >= 0));
        Assert.Equal(indices.OrderBy(i => i), indices);
    }

    [Fact]
    public void WriteCanonical_serializes_enums_camelCase()
    {
        var serializer = new PlanSerializer();
        var json = serializer.WriteCanonical(SamplePlan());

        Assert.Contains("\"selectFirst\"", json, StringComparison.Ordinal);
        Assert.Contains("\"trim\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectFirst", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_throws_PlanSerializationException_on_malformed_json()
    {
        var serializer = new PlanSerializer();

        var exception = Assert.Throws<PlanSerializationException>(() => serializer.Read("{ not json"));
        Assert.Contains("SNR-PLAN-001", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Read_rejects_versions_outside_the_readable_window(int version)
    {
        var serializer = new PlanSerializer();
        var json = serializer.WriteCanonical(SamplePlan()).Replace("\"planVersion\": 2", $"\"planVersion\": {version}", StringComparison.Ordinal);

        var exception = Assert.Throws<PlanSerializationException>(() => serializer.Read(json));

        Assert.Contains("SNR-PLAN-002", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_upgrades_a_version_1_plan_with_primary_and_fallback_locators()
    {
        var serializer = new PlanSerializer();
        var legacy = SamplePlan() with
        {
            PlanVersion = 1,
            Fields =
            [
                new FieldPlan("/Name", true, "string", [
                    new LocatorStep(PlanOperation.SelectFirst, [".name"]),
                    new LocatorStep(PlanOperation.SelectFirst, [".title"]),
                ], []),
            ],
        };
        var upgraded = serializer.Read(serializer.WriteCanonical(legacy));

        Assert.Equal(ExtractionPlan.CurrentPlanVersion, upgraded.PlanVersion);
        Assert.Equal(".name", upgraded.Fields[0].PrimaryLocator.Arguments[0]);
        Assert.Equal(".title", upgraded.Fields[0].FallbackLocator.Arguments[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Read_rejects_a_version_1_plan_with_an_ambiguous_locator_count(int locatorCount)
    {
        var serializer = new PlanSerializer();
        var locators = Enumerable.Range(0, locatorCount)
            .Select(index => new LocatorStep(PlanOperation.SelectFirst, [$".value-{index}"]))
            .ToArray();
        var legacy = SamplePlan() with
        {
            PlanVersion = 1,
            Fields = [new FieldPlan("/Name", true, "string", locators, [])],
        };

        var exception = Assert.Throws<PlanSerializationException>(() => serializer.Read(serializer.WriteCanonical(legacy)));

        Assert.Contains("SNR-PLAN-002", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteCanonical_omits_null_maxItems_and_round_trips_a_non_null_value()
    {
        var serializer = new PlanSerializer();
        var withoutCap = serializer.WriteCanonical(SamplePlan());
        var withCap = SamplePlan() with { Pagination = new PaginationSpec(PaginationStrategy.NextLink, ".next", 10, MaxItems: 250) };

        Assert.DoesNotContain("\"maxItems\"", withoutCap, StringComparison.Ordinal);
        Assert.Contains("\"maxItems\": 250", serializer.WriteCanonical(withCap), StringComparison.Ordinal);
        Assert.Equal(250, serializer.Read(serializer.WriteCanonical(withCap)).Pagination.MaxItems);
    }

    private static ExtractionPlan SamplePlan() => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
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
