namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Marks the single property that holds a schema's item collection. Exactly one property (or the
/// root, when the schema type itself is a collection) may carry this attribute — more than one is a
/// derivation error (<c>SNR-SCH-001</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeCollectionAttribute : Attribute
{
    /// <summary>Singular name used for each item, e.g. <c>"product"</c>.</summary>
    public string? ItemName { get; init; }
}
