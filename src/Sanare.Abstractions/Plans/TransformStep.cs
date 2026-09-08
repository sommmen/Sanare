namespace Sanare.Abstractions.Plans;

/// <summary>
/// A single step that transforms an already-located raw value (trimming, parsing, unit conversion, etc.).
/// See docs/features/extraction-plan-model.md ("Object model") and the closed <see cref="PlanOperation"/>
/// vocabulary for the allowed operations and their argument shapes.
/// </summary>
/// <remarks>No behaviour lives here; interpreting a transform is <c>plan-runtime</c> scope (M2).</remarks>
public sealed record TransformStep(PlanOperation Operation, IReadOnlyList<string> Arguments);
