using System.Diagnostics.CodeAnalysis;

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
public sealed record FieldPlan
{
    [SetsRequiredMembers]
    public FieldPlan(
        string pointer,
        bool required,
        string type,
        IReadOnlyList<LocatorStep> locators,
        IReadOnlyList<TransformStep> transforms)
    {
        Pointer = pointer;
        Required = required;
        Type = type;
        Locators = locators;
        Transforms = transforms;
        PrimaryLocator = locators.FirstOrDefault()!;
        FallbackLocator = locators.Skip(1).FirstOrDefault() ?? PrimaryLocator;
    }

    /// <summary>A JSON pointer into the target schema's document shape.</summary>
    public string Pointer { get; init; }

    /// <summary>Whether a missing value for this field is a runtime validation failure.</summary>
    public bool Required { get; init; }

    /// <summary>The .NET-ish type name the final value must coerce to.</summary>
    public string Type { get; init; }

    /// <summary>Alternative ways to locate the raw value, retained for legacy plan compatibility.</summary>
    public IReadOnlyList<LocatorStep> Locators { get; init; }

    /// <summary>Transforms applied in order to a located value.</summary>
    public IReadOnlyList<TransformStep> Transforms { get; init; }

    /// <summary>
    /// The named primary way to locate the raw value (plan vocabulary version 2, DR-002); tried before
    /// <see cref="FallbackLocator"/>. Alongside, not replacing, the legacy <see cref="Locators"/> list.
    /// </summary>
    public required LocatorStep PrimaryLocator { get; init; }

    /// <summary>
    /// The named fallback way to locate the raw value (plan vocabulary version 2, DR-002); tried only
    /// when <see cref="PrimaryLocator"/> fails to produce a value.
    /// </summary>
    public required LocatorStep FallbackLocator { get; init; }
}
