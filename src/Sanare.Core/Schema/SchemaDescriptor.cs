namespace Sanare.Core.Schema;

/// <summary>Immutable schema metadata used by plan validation and materialization.</summary>
public sealed record SchemaDescriptor(
    Type SchemaType,
    string Name,
    int Version,
    string JsonSchema,
    string Hash,
    IReadOnlyList<FieldDescriptor> Fields);
