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
