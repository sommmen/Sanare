using Sanare.Core.Schema;
using Sanare.Core.Schema.Quality;

namespace Sanare.Core.Tests.Schema;

public sealed class QualityReportBuilderTests
{
    [Fact]
    public void Build_flattens_collection_items_when_calculating_null_rates()
    {
        var field = new FieldDescriptor("/Items/*/Price", "Price", typeof(decimal), true, null, null, null, null);
        var schema = new SchemaDescriptor(typeof(object), "Items", 1, "{}", "sha256:test", [field], "/Items");
        var values = new Dictionary<string, object?>();
        for (var index = 0; index < 96; index++)
        {
            values[$"/Items/{index}/Price"] = index < 3 ? null : 10m;
        }

        var report = new QualityReportBuilder().Build(schema, values, ["/Other"], 0.95d);

        var health = Assert.Single(report.Fields);
        Assert.Equal(96, report.ItemCount);
        Assert.Equal(3d / 96d, health.NullRate, 10);
        Assert.Equal(93d / 96d, report.Completeness, 10);
        Assert.True(report.MeetsThreshold);
        Assert.Equal(["/Other"], report.UnmappedFields);
    }

    [Fact]
    public void Build_reports_empty_collection_as_incomplete()
    {
        var field = new FieldDescriptor("/Items/*/Price", "Price", typeof(decimal), true, null, null, null, null);
        var schema = new SchemaDescriptor(typeof(object), "Items", 1, "{}", "sha256:test", [field], "/Items");

        var report = new QualityReportBuilder().Build(schema, new Dictionary<string, object?>(), threshold: 0.01d);

        var health = Assert.Single(report.Fields);
        Assert.Equal(0, report.ItemCount);
        Assert.Equal(0d, report.Completeness);
        Assert.Equal(1d, health.NullRate);
        Assert.False(report.MeetsThreshold);
    }

    [Fact]
    public void Build_matches_nested_collection_wildcards_by_segment()
    {
        var field = new FieldDescriptor("/Items/*/Variants/*/Price", "Price", typeof(decimal), true, null, null, null, null);
        var schema = new SchemaDescriptor(typeof(object), "Items", 1, "{}", "sha256:test", [field], "/Items");
        var values = new Dictionary<string, object?>
        {
            ["/Items/0/Variants/0/Price"] = 10m,
            ["/Items/1/Variants/0/Price"] = 20m,
        };

        var report = new QualityReportBuilder().Build(schema, values);

        var health = Assert.Single(report.Fields);
        Assert.Equal(2, report.ItemCount);
        Assert.Equal(0d, health.NullRate);
        Assert.True(health.Present);
    }

    [Fact]
    public void Build_does_not_match_wildcards_across_extra_segments()
    {
        var field = new FieldDescriptor("/Items/*/Price", "Price", typeof(decimal), true, null, null, null, null);
        var schema = new SchemaDescriptor(typeof(object), "Items", 1, "{}", "sha256:test", [field], "/Items");
        var values = new Dictionary<string, object?>
        {
            ["/Items/0/Sub/Price"] = 10m,
        };

        var report = new QualityReportBuilder().Build(schema, values);

        var health = Assert.Single(report.Fields);
        Assert.Equal(1, report.ItemCount);
        Assert.Equal(1d, health.NullRate);
        Assert.False(health.Present);
    }
}
