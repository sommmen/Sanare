namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Overrides the culture used to coerce a field's (or an entire schema's) raw text values, e.g.
/// <c>"nl-NL"</c>. Resolution order is property → declaring type → source default →
/// <see cref="ScrapeRequest.Culture"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeCultureAttribute(string culture) : Attribute
{
    /// <summary>The declared culture name, e.g. <c>"nl-NL"</c>.</summary>
    public string Culture { get; } = culture;
}
