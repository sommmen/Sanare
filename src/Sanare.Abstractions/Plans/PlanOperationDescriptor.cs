namespace Sanare.Abstractions.Plans;

/// <summary>
/// The kind of value an operation argument must contain, per
/// docs/features/extraction-plan-model.md ("The closed operation vocabulary", rule 2).
/// </summary>
public enum PlanArgumentKind
{
    /// <summary>A CSS/XPath/JSON-path selector string, dialect implied by the owning operation.</summary>
    Selector,

    /// <summary>An opaque literal string used verbatim (e.g. an enum-map key, a split delimiter).</summary>
    Literal,

    /// <summary>A regular-expression pattern.</summary>
    Pattern,

    /// <summary>A unit symbol understood by <c>convertUnit</c>/<c>stripUnit</c> (e.g. <c>"GB"</c>, <c>"mAh"</c>).</summary>
    Unit,

    /// <summary>A culture name (e.g. <c>"nl-NL"</c>) overriding the plan's default culture for this step.</summary>
    Culture,

    /// <summary>A JSON pointer, typically the target of a multi-output transform such as <c>stripCurrency</c>.</summary>
    Pointer,

    /// <summary>An integer literal (e.g. a capture-group index for <c>regexCapture</c>).</summary>
    Int,
}

/// <summary>
/// Static metadata describing the shape and constraints of a single <see cref="PlanOperation"/> member:
/// its conceptual category, argument arity and kinds, and which acquisition tiers may use it.
/// See docs/features/extraction-plan-model.md rules 2–5.
/// </summary>
/// <remarks>
/// This descriptor only carries data; validating a real <see cref="ExtractionPlan"/> against it and
/// executing the operation are <c>plan-runtime</c> concerns (M2, not implemented in v0.1). The v0.1
/// scope is limited to making sure every <see cref="PlanOperation"/> member has exactly one descriptor
/// in <see cref="PlanOperationCatalog"/>, so the catalogue can never silently drift from the enum.
/// </remarks>
/// <param name="Operation">The operation this descriptor describes.</param>
/// <param name="Category">The operation's conceptual grouping; only <see cref="PlanOperationCategory.Locator"/> locates content, everything else transforms an already-located value (rule 2's "locator or transform" distinction).</param>
/// <param name="MinArguments">The minimum number of arguments this operation accepts.</param>
/// <param name="MaxArguments">The maximum number of arguments this operation accepts.</param>
/// <param name="ArgumentKinds">The kind of each positional argument, in order; when fewer than <paramref name="MaxArguments"/>, the last kind repeats for trailing variadic arguments.</param>
/// <param name="AllowedTiers">The acquisition tiers permitted to use this operation.</param>
public sealed record PlanOperationDescriptor(
    PlanOperation Operation,
    PlanOperationCategory Category,
    int MinArguments,
    int MaxArguments,
    IReadOnlyList<PlanArgumentKind> ArgumentKinds,
    IReadOnlyList<AcquisitionTier> AllowedTiers)
{
    /// <summary>Whether this operation locates a node, attribute, or value within a document.</summary>
    public bool IsLocator => Category == PlanOperationCategory.Locator;
}
