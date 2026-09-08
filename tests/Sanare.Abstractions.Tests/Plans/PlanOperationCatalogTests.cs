using Sanare.Abstractions.Plans;

namespace Sanare.Abstractions.Tests.Plans;

public sealed class PlanOperationCatalogTests
{
    [Fact]
    public void Every_plan_operation_member_has_exactly_one_descriptor()
    {
        var operations = Enum.GetValues<PlanOperation>();

        foreach (var operation in operations)
        {
            var descriptor = PlanOperationCatalog.Get(operation);
            Assert.Equal(operation, descriptor.Operation);
        }

        Assert.Equal(operations.Length, PlanOperationCatalog.All.Count);
    }

    [Fact]
    public void Browser_only_operations_are_restricted_to_the_browser_tier()
    {
        PlanOperation[] browserOnlyOperations =
        [
            PlanOperation.Click,
            PlanOperation.WaitForSelector,
            PlanOperation.WaitForNetworkIdle,
            PlanOperation.Scroll,
            PlanOperation.SelectOption,
            PlanOperation.Type,
        ];

        foreach (var operation in browserOnlyOperations)
        {
            var descriptor = PlanOperationCatalog.Get(operation);
            Assert.Equal([AcquisitionTier.Browser], descriptor.AllowedTiers);
        }
    }

    [Fact]
    public void JsonPath_requires_json_content()
    {
        var descriptor = PlanOperationCatalog.Get(PlanOperation.JsonPath);

        Assert.DoesNotContain(AcquisitionTier.Html, descriptor.AllowedTiers);
    }
}
