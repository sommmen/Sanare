namespace Sanare.Abstractions.Plans;

/// <summary>
/// The extraction recipe for a single schema field: where to locate its raw value(s) and how to
/// transform them into the schema's declared <see cref="Type"/>. See
/// docs/features/extraction-plan-model.md ("Object model") and docs/sanare/tech-design.md §10.1.1.
/// </summary>
/// <param name="Pointer">
/// A JSON pointer into the schema's document shape, e.g. <c>/products/-/name</c>. Validated for
/// well-formedness and, when a schema is supplied, schema membership by <c>IPlanValidator</c>.
/// </param>
/// <param name="Required">Whether a missing value for this field is a validation failure at run time.</param>
/// <param name="Locators">
/// One or more alternative ways to locate the raw value; the runtime tries them in order and uses the
/// first that succeeds. At least one is required.
/// </param>
/// <param name="Transforms">Applied in order to the value produced by the successful locator.</param>
/// <param name="Type">The .NET-ish type name the final value must coerce to, e.g. <c>"string"</c>, <c>"decimal"</c>.</param>
public sealed record FieldPlan(
    string Pointer,
    bool Required,
    string Type,
    IReadOnlyList<LocatorStep> Locators,
    IReadOnlyList<TransformStep> Transforms);
