using Sanare.Abstractions.Attributes;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;

namespace Sanare.Core.Tests.Schema;

public sealed class TypeCoercerTests
{
    [Fact]
    public void TryCoerce_Strips_declared_unit_before_parsing()
    {
        var field = Assert.Single(new SchemaDeriver().Derive<Capacity>().Fields);

        var success = new TypeCoercer().TryCoerce("  5,000 mAh ", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(5000, result);
    }

    [Fact]
    public void TryCoerce_Rejects_unit_residue()
    {
        var field = Assert.Single(new SchemaDeriver().Derive<Capacity>().Fields);

        var success = new TypeCoercer().TryCoerce("5000 Wh", field, out _, out var error);

        Assert.False(success);
        Assert.Contains("Expected unit", error, StringComparison.Ordinal);
    }

    private sealed class Capacity
    {
        [ScrapeUnit("mAh")]
        public int Value { get; set; }
    }
}
