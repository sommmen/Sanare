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
}
