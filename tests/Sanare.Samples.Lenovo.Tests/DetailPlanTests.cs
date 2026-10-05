using Sanare.Core.Plans;
using Sanare.Core.Schema;
using Sanare.Samples.Lenovo;

namespace Sanare.Samples.Lenovo.Tests;

public sealed class DetailPlanTests
{
    [Fact]
    public void Detail_plan_is_canonical_and_valid_for_the_tablet_product_schema()
    {
        var path = Path.Combine(FindRepositoryRoot(), "samples", "Sanare.Samples.Lenovo.State", "scripts", "plans", "lenovo-com", "tablet-detail.json");
        var json = File.ReadAllText(path);
        var serializer = new PlanSerializer();
        var plan = serializer.Read(json);

        Assert.Equal(json, serializer.WriteCanonical(plan));
        Assert.True(new PlanValidator().Validate(plan, new SchemaDeriver().Derive<TabletProduct>("nl-NL")).IsValid);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sanare.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
