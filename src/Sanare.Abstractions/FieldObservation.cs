namespace Sanare.Abstractions;

/// <summary>
/// Records how a single field's value was obtained for one item during a streamed run.
/// </summary>
/// <param name="JsonPointer">JSON pointer identifying the field within the item schema.</param>
/// <param name="Present">Whether a value was found for the field on this item.</param>
/// <param name="CoercionSucceeded">Whether the raw value coerced to the declared type successfully.</param>
public sealed record FieldObservation(string JsonPointer, bool Present, bool CoercionSucceeded);
