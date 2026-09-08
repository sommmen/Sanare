namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Declares the unit a numeric field's raw text is expressed in (e.g. <c>"GB"</c>, <c>"cm"</c>), used
/// by the type coercer to strip/convert unit suffixes before parsing the underlying value.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeUnitAttribute(string unit) : Attribute
{
    /// <summary>The declared unit.</summary>
    public string Unit { get; } = unit;
}
