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
/// The ordered locator program for this field. At least one step is required.
/// <para>
/// A step is either a <em>document</em> locator (<c>SelectFirst</c>, <c>SelectAll</c>, <c>XPath</c>),
/// which starts a fresh candidate against the whole document, or a <em>value</em> locator
/// (<c>JsonPath</c>, <c>RegexCapture</c>, <c>Index</c>, <c>Split</c>, <c>Text</c>, <c>Attribute</c>,
/// <c>Html</c>), which consumes the preceding step's output. Consecutive value steps therefore form a
/// pipeline, and each document step begins a new alternative candidate; the runtime takes the first
/// candidate whose whole pipeline yields a value.
/// </para>
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
    /// The first step of <see cref="Locators"/>, surfaced as a named member for plan vocabulary version 2
    /// (DR-002). Alongside, not replacing, the <see cref="Locators"/> list.
    /// </summary>
    /// <remarks>
    /// <b>Not evaluated by the runtime.</b> <c>PlanExecutor</c> interprets <see cref="Locators"/> directly,
    /// because a locator program is a sequence of pipelines rather than a pair of independent candidates —
    /// a two-member pair cannot express <c>Html → RegexCapture → JsonPath</c>. This member is retained for
    /// serialization compatibility and is derived from <see cref="Locators"/>; it is therefore the first
    /// pipeline <em>stage</em>, which is only also the first candidate when the field has no pipeline.
    /// Representing candidates and stages distinctly is deferred to a plan-version bump; see
    /// docs/features/plan-runtime.md ("Per-field selector pairs") for the target-state contract.
    /// </remarks>
    public required LocatorStep PrimaryLocator { get; init; }

    /// <summary>
    /// The second step of <see cref="Locators"/> (or the first when there is only one), surfaced as a named
    /// member for plan vocabulary version 2 (DR-002).
    /// </summary>
    /// <remarks>
    /// <b>Not evaluated by the runtime</b>, and despite its name it is not necessarily an alternative
    /// candidate: it is simply the second locator step, which in a pipeline is the stage that consumes the
    /// first step's output. See <see cref="PrimaryLocator"/>.
    /// </remarks>
    public required LocatorStep FallbackLocator { get; init; }
}
