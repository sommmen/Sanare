using Sanare.Abstractions.Attributes;
using Sanare.Core.Schema;

namespace Sanare.Core.Tests.Schema;

public sealed class SchemaDeriverTests
{
    [Fact]
    public void Derive_Changes_the_hash_when_a_description_or_hint_changes()
    {
        var first = new SchemaDeriver().Derive<DescribedProduct>();
        var second = new SchemaDeriver().Derive<HintedProduct>();

        Assert.NotEqual(first.Hash, second.Hash);
    }

    [Fact]
    public void Derive_Uses_attributes_and_nullable_annotations()
    {
        var schema = new SchemaDeriver().Derive<Product>("en-US");

        var name = Assert.Single(schema.Fields, field => field.Name == nameof(Product.Name));
        var count = Assert.Single(schema.Fields, field => field.Name == nameof(Product.Count));
        Assert.True(name.Required);
        Assert.Equal("GBP", name.Unit);
        Assert.False(count.Required);
        Assert.DoesNotContain(schema.Fields, field => field.Name == nameof(Product.Ignored));
        Assert.StartsWith("sha256:", schema.Hash);
        Assert.Equal(71, schema.Hash.Length);
        Assert.Contains("\"properties\"", schema.JsonSchema);
    }

    private sealed class Product
    {
        [ScrapeField(Required = true)]
        [ScrapeUnit("GBP")]
        public string Name { get; set; } = string.Empty;

        public int? Count { get; set; }

        [ScrapeIgnore]
        public string Ignored { get; set; } = string.Empty;
    }

    private sealed class DescribedProduct
    {
        [ScrapeField(Description = "Original description")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class HintedProduct
    {
        [ScrapeHint("Original description")]
        public string Name { get; set; } = string.Empty;
    }
}
