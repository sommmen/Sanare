namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Removes a property, and its whole subtree, from schema derivation.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeIgnoreAttribute : Attribute;
