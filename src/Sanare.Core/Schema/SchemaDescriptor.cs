namespace Sanare.Core.Schema;

/// <summary>Immutable schema metadata used by plan validation and materialization.</summary>
public sealed record SchemaDescriptor(
    Type SchemaType,
    string Name,
    int Version,
    string JsonSchema,
    string Hash,
    IReadOnlyList<FieldDescriptor> Fields,
    string? CollectionPointer = null)
{
    /// <summary>Spec-aligned alias for <see cref="SchemaType"/>.</summary>
    public Type ClrType => SchemaType;

    /// <summary>Spec-aligned alias for <see cref="Name"/>.</summary>
    public string SchemaName => Name;

    /// <summary>Spec-aligned alias for <see cref="Version"/>.</summary>
    public int SchemaVersion => Version;

    /// <summary>Spec-aligned alias for <see cref="Hash"/>.</summary>
    public string SchemaHash => Hash;
}
