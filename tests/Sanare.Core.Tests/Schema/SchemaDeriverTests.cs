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
        Assert.Equal(typeof(int), count.ClrType);
        Assert.DoesNotContain(schema.Fields, field => field.Name == nameof(Product.Ignored));
        Assert.StartsWith("sha256:", schema.Hash);
        Assert.Equal(71, schema.Hash.Length);
        Assert.Contains("\"properties\"", schema.JsonSchema);
    }

    [Fact]
    public void Derive_validates_effective_property_cultures()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new SchemaDeriver().Derive<InvalidCultureProduct>());

        Assert.Contains("culture", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Derive_keeps_scalar_collections_as_field_descriptors()
    {
        var schema = new SchemaDeriver().Derive<ScalarCollectionProduct>();

        var items = Assert.Single(schema.Fields);
        Assert.Equal("/Items", items.JsonPointer);
        Assert.Equal(typeof(string[]), items.ClrType);
        Assert.Equal("/Items", schema.CollectionPointer);
    }

    [Fact]
    public void Derive_recurses_into_complex_collection_elements()
    {
        var schema = new SchemaDeriver().Derive<VariantCollectionProduct>();

        var price = Assert.Single(schema.Fields);
        Assert.Equal("/Items/*/Price", price.JsonPointer);
        Assert.Equal(typeof(decimal), price.ClrType);
        Assert.Equal("/Items", schema.CollectionPointer);
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

    private sealed class InvalidCultureProduct
    {
        [ScrapeCulture("invalid_culture")]
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ScalarCollectionProduct
    {
        [ScrapeCollection]
        public string[] Items { get; set; } = [];
    }

    private sealed class VariantCollectionProduct
    {
        [ScrapeCollection]
        public List<Variant> Items { get; set; } = [];
    }

    private sealed class Variant
    {
        public decimal Price { get; set; }
    }
}
