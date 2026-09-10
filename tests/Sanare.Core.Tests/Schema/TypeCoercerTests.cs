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

    [Fact]
    public void TryCoerce_Converts_declared_units_for_int_target()
    {
        var field = new FieldDescriptor("/Mass", "Mass", typeof(int), true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("1000 g", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(1, result);
    }

    [Fact]
    public void TryCoerce_Converts_declared_units_for_long_target()
    {
        var field = new FieldDescriptor("/Mass", "Mass", typeof(long), true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("1000 g", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(1L, result);
    }

    [Fact]
    public void TryCoerce_Converts_declared_units_for_decimal_target()
    {
        var field = new FieldDescriptor("/Mass", "Mass", typeof(decimal), true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("1000 g", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(1m, result);
    }

    [Fact]
    public void TryCoerce_Converts_declared_units_for_double_target()
    {
        var field = new FieldDescriptor("/Mass", "Mass", typeof(double), true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("1000 g", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(1d, result);
    }

    [Fact]
    public void TryCoerce_Converts_declared_units_for_float_target()
    {
        var field = new FieldDescriptor("/Mass", "Mass", typeof(float), true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("1000 g", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(1f, result);
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    public void TryCoerce_Rejects_fractional_converted_integer(Type targetType)
    {
        var field = new FieldDescriptor("/Mass", "Mass", targetType, true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("500 g", field, out _, out var error);

        Assert.False(success);
        Assert.Contains("Cannot convert unit", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(int))]
    [InlineData(typeof(long))]
    public void TryCoerce_Rejects_incompatible_unit_for_integer_target(Type targetType)
    {
        var field = new FieldDescriptor("/Mass", "Mass", targetType, true, null, "kg", null, null);

        var success = new TypeCoercer().TryCoerce("5 m", field, out _, out var error);

        Assert.False(success);
        Assert.Contains("Expected unit", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1.299,00 €", "nl-NL", 1299d)]
    [InlineData("1,299.00 $", "en-US", 1299d)]
    public void Coerce_parses_culture_specific_currency(string raw, string cultureName, decimal expected)
    {
        var field = new FieldDescriptor("/Price", "Price", typeof(decimal), true, null, null, cultureName, null);

        var outcome = new TypeCoercer().Coerce(raw, field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal(expected, outcome.Value!.GetValue<decimal>());
    }

    [Theory]
    [InlineData("ja", true)]
    [InlineData("nee", false)]
    [InlineData("✓", true)]
    [InlineData("✗", false)]
    public void Coerce_uses_culture_truth_table(string raw, bool expected)
    {
        var field = new FieldDescriptor("/Available", "Available", typeof(bool), true, null, null, "nl-NL", null);

        var outcome = new TypeCoercer().Coerce(raw, field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal(expected, outcome.Value!.GetValue<bool>());
    }

    [Fact]
    public void TryCoerce_Strips_unit_from_nullable_numeric_field()
    {
        var field = new FieldDescriptor("/OptionalCapacity", "OptionalCapacity", typeof(int?), false, null, "mAh", null, null);

        var success = new TypeCoercer().TryCoerce("2500 mAh", field, out var result, out var error);

        Assert.True(success, error);
        Assert.Equal(2500, result);
    }

    [Fact]
    public void Coerce_creates_suffixes_for_duplicate_dictionary_labels()
    {
        var field = new FieldDescriptor("/Details", "Details", typeof(IReadOnlyDictionary<string, string>), false, null, null, null, null);

        var outcome = new TypeCoercer().Coerce(" Size : Large; Size: Small ", field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        var values = outcome.Value!.AsObject();
        Assert.Equal("Large", values["Size"]!.GetValue<string>());
        Assert.Equal("Small", values["Size#2"]!.GetValue<string>());
    }

    [Fact]
    public void Coerce_returns_empty_array_for_blank_collection_values()
    {
        var field = new FieldDescriptor("/Values", "Values", typeof(int[]), true, null, null, null, null);

        var outcome = new TypeCoercer().Coerce("   ", field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Empty(outcome.Value!.AsArray());
    }

    [Fact]
    public void Coerce_returns_empty_object_for_blank_dictionary_values()
    {
        var field = new FieldDescriptor("/Details", "Details", typeof(Dictionary<string, string>), true, null, null, null, null);

        var outcome = new TypeCoercer().Coerce("   ", field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Empty(outcome.Value!.AsObject());
    }

    [Fact]
    public void Coerce_uses_raw_node_values_for_collection_overload()
    {
        var field = new FieldDescriptor("/Prices", "Prices", typeof(IReadOnlyList<decimal>), false, null, null, "nl-NL", null);

        var outcome = new TypeCoercer().Coerce(["1,50", "2,75"], field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        var values = outcome.Value!.AsArray();
        Assert.Equal(1.50m, values[0]!.GetValue<decimal>());
        Assert.Equal(2.75m, values[1]!.GetValue<decimal>());
    }

    [Theory]
    [InlineData(typeof(Dictionary<string, string>))]
    [InlineData(typeof(IDictionary<string, string>))]
    [InlineData(typeof(IReadOnlyDictionary<string, string>))]
    [InlineData(typeof(CustomDictionary))]
    public void Coerce_accepts_supported_string_dictionary_shapes(Type dictionaryType)
    {
        var field = new FieldDescriptor("/Details", "Details", dictionaryType, false, null, null, null, null);

        var outcome = new TypeCoercer().Coerce("Color: Blue", field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("Blue", outcome.Value!.AsObject()["Color"]!.GetValue<string>());
    }

    [Fact]
    public void Coerce_serializes_enum_values_as_names()
    {
        var field = new FieldDescriptor("/Status", "Status", typeof(Availability), true, null, null, null, null);

        var outcome = new TypeCoercer().Coerce("InStock", field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal("InStock", outcome.Value!.GetValue<string>());
    }

    [Theory]
    [InlineData("2024-01-02T03:04:05", "2024-01-02T03:04:05.0000000Z")]
    [InlineData("2024-01-02T04:04:05+01:00", "2024-01-02T03:04:05.0000000Z")]
    public void Coerce_parses_date_time_values_as_utc(string raw, string expected)
    {
        var field = new FieldDescriptor("/ObservedAt", "ObservedAt", typeof(DateTime), true, null, null, null, null);

        var outcome = new TypeCoercer().Coerce(raw, field, new CoercionContext());

        Assert.True(outcome.Success, outcome.FailureReason);
        Assert.Equal(expected, outcome.Value!.GetValue<string>());
    }

    private sealed class Capacity
    {
        [ScrapeUnit("mAh")]
        public int Value { get; set; }
    }

    private sealed class CustomDictionary : Dictionary<string, string>
    {
    }

    private enum Availability
    {
        InStock,
        SoldOut,
    }
}
