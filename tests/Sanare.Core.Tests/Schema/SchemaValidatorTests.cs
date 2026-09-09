using System.Text.Json.Nodes;
using Sanare.Core.Schema;

namespace Sanare.Core.Tests.Schema;

public sealed class SchemaValidatorTests
{
    [Fact]
    public void Validate_returns_all_required_and_structural_violations()
    {
        var fields = new[]
        {
            new FieldDescriptor("/Name", "Name", typeof(string), true, null, null, null, null),
            new FieldDescriptor("/Count", "Count", typeof(int), true, null, null, null, null),
        };
        var schema = new SchemaDescriptor(typeof(object), "Example", 1, "{}", "sha256:test", fields);
        var result = new SchemaValidator().Validate(new JsonObject { ["Count"] = "bad" }, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, violation => violation.JsonPointer == "/Name" && violation.Code == "SNR-SCH-004");
        Assert.Contains(result.Violations, violation => violation.JsonPointer == "/Count" && violation.Code == "SNR-SCH-002");
    }
}
