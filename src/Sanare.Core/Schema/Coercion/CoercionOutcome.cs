using System.Text.Json.Nodes;

namespace Sanare.Core.Schema.Coercion;

/// <summary>Captures a value conversion, including its normalised unit or deterministic failure.</summary>
public readonly record struct CoercionOutcome(
    bool Success,
    JsonNode? Value,
    string? RawValue,
    string? NormalisedUnit,
    string? FailureReason);
