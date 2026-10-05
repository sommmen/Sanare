using System.Text.Json;
using Sanare.Core.Schema;
using Sanare.Samples.Lenovo;

namespace Sanare.Samples.Lenovo.Tests;

public sealed class TabletSchemasTests
{
    private static readonly Lock ConsoleLock = new();

    [Fact]
    public void Derive_maps_all_lenovo_schema_shapes()
    {
        var deriver = new SchemaDeriver();

        var listing = deriver.Derive<TabletListing>("nl-NL");
        var product = deriver.Derive<TabletProduct>("nl-NL");
        var specification = deriver.Derive<ProductSpecification>("nl-NL");

        Assert.Equal("TabletListing", listing.Name);
        Assert.Contains(listing.Fields, field => field.JsonPointer == "/ProductUrl" && field.Required);
        Assert.Contains(listing.Fields, field => field.JsonPointer == "/Price" && field.ClrType == typeof(decimal));
        Assert.Equal("/Specifications", product.CollectionPointer);
        Assert.Contains(product.Fields, field => field.JsonPointer == "/Specifications/*/Name" && field.Required);
        Assert.Contains(product.Fields, field => field.JsonPointer == "/Specifications/*/Value" && field.Required);
        Assert.Null(specification.CollectionPointer);
        Assert.Collection(specification.Fields.OrderBy(field => field.JsonPointer),
            field => Assert.Equal("/Group", field.JsonPointer),
            field => Assert.Equal("/Name", field.JsonPointer),
            field => Assert.Equal("/Value", field.JsonPointer));
    }

    [Fact]
    public void Detail_offline_writes_the_extracted_product_as_json()
    {
        lock (ConsoleLock)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                Assert.Equal(0, Program.Main(["detail", "--offline"]));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            using var document = JsonDocument.Parse(stdout.ToString());
            var root = document.RootElement;
            Assert.Equal("Lenovo Yoga Tab Gen 2", root.GetProperty("name").GetString());
            Assert.Equal(649.01m, root.GetProperty("price").GetDecimal());
            Assert.Equal("EUR", root.GetProperty("currency").GetString());
            var specifications = root.GetProperty("specifications").EnumerateArray().ToArray();
            Assert.True(specifications.Length >= 14);
            Assert.All(specifications, specification =>
            {
                Assert.False(string.IsNullOrWhiteSpace(specification.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(specification.GetProperty("value").GetString()));
            });
            Assert.Equal(string.Empty, stderr.ToString());
        }
    }

    [Fact]
    public void Program_writes_usage_only_to_stderr()
    {
        lock (ConsoleLock)
        {
            var stdout = new StringWriter();
            var stderr = new StringWriter();
            var originalOut = Console.Out;
            var originalError = Console.Error;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);

                Assert.Equal(0, Program.Main([]));
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            Assert.Equal(string.Empty, stdout.ToString());
            Assert.Contains("Usage:", stderr.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Json_context_writes_camel_case_properties_and_numeric_prices()
    {
        var product = new TabletProduct
        {
            Name = "Lenovo Yoga Tab Gen 2",
            Price = 649.01m,
            Currency = "EUR",
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(product, LenovoJsonContext.Default.TabletProduct));
        var root = document.RootElement;

        Assert.Equal("Lenovo Yoga Tab Gen 2", root.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Number, root.GetProperty("price").ValueKind);
        Assert.Equal("EUR", root.GetProperty("currency").GetString());
    }
}
