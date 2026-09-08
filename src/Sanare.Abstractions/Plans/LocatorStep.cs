namespace Sanare.Abstractions.Plans;

/// <summary>
/// A single step that locates a raw value within acquired content (a selector, an XPath, a JSON path,
/// etc.). See docs/features/extraction-plan-model.md ("Object model") and the closed
/// <see cref="PlanOperation"/> vocabulary for the allowed operations and their argument shapes.
/// </summary>
/// <remarks>No behaviour lives here; interpreting a locator is <c>plan-runtime</c> scope (M2).</remarks>
public sealed record LocatorStep(PlanOperation Operation, IReadOnlyList<string> Arguments);
