using Sanare.Core.Schema;

namespace Sanare.Core.Tests.Schema;

public sealed class SchemaHasherTests
{
    [Fact]
    public void CreateCanonicalJson_and_hash_are_deterministic_regardless_of_input_order()
    {
        var price = new FieldDescriptor("/Price", "Price", typeof(decimal), true, "Amount", null, "en-US", "money");
        var name = new FieldDescriptor("/Name", "Name", typeof(string), true, null, null, null, null);

        var first = SchemaHasher.CreateCanonicalJson("Product", 1, [price, name]);
        var second = SchemaHasher.CreateCanonicalJson("Product", 1, [name, price]);
        var firstDescriptor = new SchemaDescriptor(typeof(object), "Product", 1, first, string.Empty, [price, name]);
        var secondDescriptor = new SchemaDescriptor(typeof(object), "Product", 1, second, string.Empty, [name, price]);

        Assert.Equal(first, second);
        Assert.Equal(SchemaHasher.Compute(firstDescriptor), SchemaHasher.Compute(secondDescriptor));
    }
}
