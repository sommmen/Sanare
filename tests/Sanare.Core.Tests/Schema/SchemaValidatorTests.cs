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

    [Fact]
    public void Validate_resolves_wildcard_pointers_for_array_items()
    {
        var fields = new[]
        {
            new FieldDescriptor("/Items/*/Price", "Price", typeof(decimal), true, null, null, null, null),
        };
        var schema = new SchemaDescriptor(typeof(object), "Example", 1, "{}", "sha256:test", fields);

        // Create a JSON document with an array where one item is missing a required Price field
        var items = new JsonArray();
        var item0 = new JsonObject();
        item0["Price"] = JsonValue.Create(10m);
        items.Add(item0);

        var item1 = new JsonObject();
        // Item1 intentionally does NOT have Price
        items.Add(item1);

        var item2 = new JsonObject();
        item2["Price"] = JsonValue.Create(20m);
        items.Add(item2);

        var document = new JsonObject();
        document["Items"] = items;

        var result = new SchemaValidator().Validate(document, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, v => v.JsonPointer == "/Items/*/Price" && v.Code == "SNR-SCH-004");
    }

    [Fact]
    public void Validate_continues_after_required_null_nodes()
    {
        var fields = new[]
        {
            new FieldDescriptor("/Items/*/Price", "Price", typeof(decimal), true, null, null, null, null),
        };
        var schema = new SchemaDescriptor(typeof(object), "Example", 1, "{}", "sha256:test", fields);
        var document = new JsonObject
        {
            ["Items"] = new JsonArray
            {
                new JsonObject { ["Price"] = null },
                new JsonObject { ["Price"] = "not a decimal" },
            },
        };

        var result = new SchemaValidator().Validate(document, schema);

        Assert.False(result.IsValid);
        Assert.Contains(result.Violations, violation => violation.Code == "SNR-SCH-004");
        Assert.Contains(result.Violations, violation => violation.Code == "SNR-SCH-002");
    }

    [Theory]
    [InlineData(typeof(Dictionary<string, string>))]
    [InlineData(typeof(IDictionary<string, string>))]
    [InlineData(typeof(IReadOnlyDictionary<string, string>))]
    [InlineData(typeof(CustomDictionary))]
    public void Validate_accepts_supported_string_dictionary_shapes(Type dictionaryType)
    {
        var fields = new[]
        {
            new FieldDescriptor("/Details", "Details", dictionaryType, true, null, null, null, null),
        };
        var schema = new SchemaDescriptor(typeof(object), "Example", 1, "{}", "sha256:test", fields);
        var document = new JsonObject
        {
            ["Details"] = new JsonObject { ["Color"] = "Blue" },
        };

        var result = new SchemaValidator().Validate(document, schema);

        Assert.True(result.IsValid);
    }

    private sealed class CustomDictionary : Dictionary<string, string>
    {
    }
}
