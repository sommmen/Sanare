namespace Sanare.Core.Schema;

/// <summary>Describes a field derived from a schema type.</summary>
public sealed record FieldDescriptor(
    string JsonPointer,
    string Name,
    Type ClrType,
    bool Required,
    string? Description,
    string? Unit,
    string Culture,
    string? Hint);
