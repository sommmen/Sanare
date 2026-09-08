namespace Sanare.Abstractions.Quality;

/// <summary>
/// Per-field health observation contributing to a <see cref="QualityReport"/>.
/// </summary>
/// <param name="JsonPointer">JSON pointer identifying the field within the schema.</param>
/// <param name="Required">Whether the field is required by the schema.</param>
/// <param name="Present">Whether a value was found for the field.</param>
/// <param name="CoercionSucceeded">Whether the raw value coerced to the declared type successfully.</param>
/// <param name="NullRate">Observed null rate for this field across the run (0..1).</param>
public sealed record FieldHealth(
    string JsonPointer,
    bool Required,
    bool Present,
    bool CoercionSucceeded,
    double NullRate);
