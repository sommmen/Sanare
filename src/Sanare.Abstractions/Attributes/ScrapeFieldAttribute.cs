namespace Sanare.Abstractions.Attributes;

/// <summary>
/// Annotates a schema property (or the schema type itself) with derivation metadata. On a type,
/// only <see cref="Version"/> is meaningful (it becomes the schema's <c>SchemaVersion</c>, defaulting
/// to 1 when absent). On a property, <see cref="Description"/> and <see cref="Required"/> apply.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ScrapeFieldAttribute : Attribute
{
    /// <summary>Human-readable description surfaced in the derived JSON schema.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Marks a field required in addition to the usual non-nullable-type inference. A C# attribute
    /// cannot represent a nullable Boolean named argument, so v0.1 intentionally supports the explicit
    /// <c>true</c> case only; nullable fields remain optional unless this is set.
    /// </summary>
    public bool Required { get; init; }

    /// <summary>Schema version, only meaningful when applied to the schema type. Defaults to 1.</summary>
    public int Version { get; init; } = 1;
}
