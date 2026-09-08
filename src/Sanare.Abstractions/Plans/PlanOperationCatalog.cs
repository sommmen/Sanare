namespace Sanare.Abstractions.Plans;

/// <summary>
/// The documented conceptual grouping of a <see cref="PlanOperation"/> member, matching the comment
/// sections in docs/features/extraction-plan-model.md ("The closed operation vocabulary"). This is
/// descriptive metadata only: it does not by itself constrain whether an operation may be placed in a
/// <see cref="FieldPlan.Locators"/> or <see cref="FieldPlan.Transforms"/> list — the canonical JSON example
/// (docs/sanare/tech-design.md §10.1.1) shows <see cref="PlanOperation.Text"/> used as a transform despite
/// being grouped with the locator-shaped operations, so list placement is a per-field authoring decision
/// validated structurally (arity/kind/tier), not a fixed per-operation role.
/// </summary>
public enum PlanOperationCategory
{
    /// <summary>Reads or navigates a document to obtain a node, attribute, or raw value.</summary>
    Locator,

    /// <summary>Normalises or cleans an already-extracted string.</summary>
    TextTransform,

    /// <summary>Parses a cleaned string into a typed value.</summary>
    Parser,

    /// <summary>Derives a semantically different value (URL resolution, enum mapping, unit conversion).</summary>
    SemanticTransform,

    /// <summary>Builds or manipulates structured/collection-shaped output.</summary>
    Structural,

    /// <summary>Drives the browser tier (clicking, waiting, typing); never valid outside <see cref="AcquisitionTier.Browser"/>.</summary>
    Interaction,
}

/// <summary>
/// The complete, static metadata catalogue for every <see cref="PlanOperation"/> member. See
/// docs/features/extraction-plan-model.md rules 2–5.
/// </summary>
/// <remarks>
/// <para>
/// v0.1 scope note: the documented rules fix argument <em>kinds</em> vocabulary and the tier restrictions
/// called out explicitly (interaction ops are browser-only; <see cref="PlanOperation.JsonPath"/> requires
/// JSON content; <see cref="PlanOperation.XPath"/>/<see cref="PlanOperation.SelectFirst"/>/
/// <see cref="PlanOperation.SelectAll"/> require markup) but do not spell out every operation's exact
/// argument arity. Where the docs do not state an arity, it has been inferred from the canonical JSON
/// example (docs/sanare/tech-design.md §10.1.1) and the plan-runtime operation list
/// (docs/features/plan-runtime.md); confirming or refining these is <c>plan-runtime</c> (M2) scope, not
/// v0.1. Similarly, tiers are only restricted where the docs explicitly say so — everything else is left
/// available to all four tiers rather than inventing an unstated restriction.
/// </para>
/// <para>
/// This catalogue is inert data. It is used by a v0.1 unit test to assert that every
/// <see cref="PlanOperation"/> member has exactly one descriptor here (so a new enum member cannot be
/// added silently). Actually validating a real (e.g. LLM- or JSON-authored) plan against it is
/// <c>IPlanValidator</c>, which belongs to the full <c>extraction-plan-model</c> feature together with the
/// plan JSON serializer, canonical writer, and content hash — deliberately deferred past v0.1 because
/// v0.1 plans are hand-authored in-process (<see cref="Sanare.Core.InMemoryExtractionPlanProvider"/>),
/// not deserialized from untrusted JSON, so the security boundary this catalogue metadata exists to
/// support has no caller yet. Execution against a validated plan is the separately out-of-scope
/// <c>plan-runtime</c> feature (M2).
/// </para>
/// </remarks>
public static class PlanOperationCatalog
{
    /// <summary>All four acquisition tiers, used where an operation is not tier-restricted.</summary>
    private static readonly IReadOnlyList<AcquisitionTier> AllTiers =
    [
        AcquisitionTier.JsonApi, AcquisitionTier.StructuredData, AcquisitionTier.Html, AcquisitionTier.Browser,
    ];

    private static readonly IReadOnlyList<AcquisitionTier> JsonTiers =
        [AcquisitionTier.JsonApi, AcquisitionTier.StructuredData];

    private static readonly IReadOnlyList<AcquisitionTier> MarkupTiers =
        [AcquisitionTier.StructuredData, AcquisitionTier.Html, AcquisitionTier.Browser];

    private static readonly IReadOnlyList<AcquisitionTier> BrowserOnly = [AcquisitionTier.Browser];

    private static readonly IReadOnlyDictionary<PlanOperation, PlanOperationDescriptor> Descriptors =
        BuildDescriptors();

    /// <summary>All descriptors, one per <see cref="PlanOperation"/> member.</summary>
    public static IReadOnlyCollection<PlanOperationDescriptor> All => (IReadOnlyCollection<PlanOperationDescriptor>)Descriptors.Values;

    /// <summary>Looks up the descriptor for <paramref name="operation"/>.</summary>
    /// <exception cref="KeyNotFoundException">
    /// The operation has no descriptor — this indicates <see cref="PlanOperationCatalog"/> was not
    /// updated alongside a new <see cref="PlanOperation"/> member.
    /// </exception>
    public static PlanOperationDescriptor Get(PlanOperation operation) => Descriptors[operation];

    private static IReadOnlyDictionary<PlanOperation, PlanOperationDescriptor> BuildDescriptors()
    {
        PlanOperationDescriptor Locator(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds, IReadOnlyList<AcquisitionTier> tiers) =>
            new(op, PlanOperationCategory.Locator, min, max, kinds, tiers);

        PlanOperationDescriptor TextTransform(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds) =>
            new(op, PlanOperationCategory.TextTransform, min, max, kinds, AllTiers);

        PlanOperationDescriptor Parser(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds) =>
            new(op, PlanOperationCategory.Parser, min, max, kinds, AllTiers);

        PlanOperationDescriptor SemanticTransform(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds) =>
            new(op, PlanOperationCategory.SemanticTransform, min, max, kinds, AllTiers);

        PlanOperationDescriptor Structural(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds) =>
            new(op, PlanOperationCategory.Structural, min, max, kinds, AllTiers);

        PlanOperationDescriptor Interaction(PlanOperation op, int min, int max, IReadOnlyList<PlanArgumentKind> kinds) =>
            new(op, PlanOperationCategory.Interaction, min, max, kinds, BrowserOnly);

        var descriptors = new[]
        {
            // Locators
            Locator(PlanOperation.SelectFirst, 1, 1, [PlanArgumentKind.Selector], MarkupTiers),
            Locator(PlanOperation.SelectAll, 1, 1, [PlanArgumentKind.Selector], MarkupTiers),
            Locator(PlanOperation.XPath, 1, 1, [PlanArgumentKind.Selector], MarkupTiers),
            Locator(PlanOperation.JsonPath, 1, 1, [PlanArgumentKind.Selector], JsonTiers),
            Locator(PlanOperation.RegexCapture, 1, 2, [PlanArgumentKind.Pattern, PlanArgumentKind.Int], AllTiers),
            Locator(PlanOperation.Attribute, 2, 2, [PlanArgumentKind.Selector, PlanArgumentKind.Literal], AllTiers),
            Locator(PlanOperation.Text, 1, 1, [PlanArgumentKind.Selector], AllTiers),
            Locator(PlanOperation.Html, 0, 0, [], AllTiers),

            // Text transforms
            TextTransform(PlanOperation.Trim, 0, 0, []),
            TextTransform(PlanOperation.CollapseWhitespace, 0, 0, []),
            TextTransform(PlanOperation.StripCurrency, 0, 1, [PlanArgumentKind.Pointer]),
            TextTransform(PlanOperation.StripUnit, 0, 2, [PlanArgumentKind.Unit, PlanArgumentKind.Pointer]),

            // Parsers
            Parser(PlanOperation.ParseInt, 0, 1, [PlanArgumentKind.Culture]),
            Parser(PlanOperation.ParseDecimal, 0, 1, [PlanArgumentKind.Culture]),
            Parser(PlanOperation.ParseBool, 0, 0, []),
            Parser(PlanOperation.ParseDate, 0, 2, [PlanArgumentKind.Culture, PlanArgumentKind.Literal]),

            // Semantic transforms
            SemanticTransform(PlanOperation.ResolveUrl, 0, 0, []),
            SemanticTransform(PlanOperation.MapEnum, 2, int.MaxValue, [PlanArgumentKind.Literal]),
            SemanticTransform(PlanOperation.ConvertUnit, 2, 2, [PlanArgumentKind.Unit, PlanArgumentKind.Unit]),

            // Structural
            Structural(PlanOperation.KeyValueTable, 0, 0, []),
            Structural(PlanOperation.DefinitionList, 0, 0, []),
            Structural(PlanOperation.Concat, 0, 1, [PlanArgumentKind.Literal]),
            Structural(PlanOperation.Split, 1, 1, [PlanArgumentKind.Literal]),
            Structural(PlanOperation.Index, 1, 1, [PlanArgumentKind.Int]),
            Structural(PlanOperation.Coalesce, 0, 0, []),
            Structural(PlanOperation.Exists, 0, 0, []),
            Structural(PlanOperation.NotFoundPredicate, 0, 0, []),

            // Browser-tier interactions
            Interaction(PlanOperation.Click, 1, 1, [PlanArgumentKind.Selector]),
            Interaction(PlanOperation.WaitForSelector, 1, 1, [PlanArgumentKind.Selector]),
            Interaction(PlanOperation.WaitForNetworkIdle, 0, 1, [PlanArgumentKind.Int]),
            Interaction(PlanOperation.Scroll, 0, 1, [PlanArgumentKind.Int]),
            Interaction(PlanOperation.SelectOption, 2, 2, [PlanArgumentKind.Selector, PlanArgumentKind.Literal]),
            Interaction(PlanOperation.Type, 2, 2, [PlanArgumentKind.Selector, PlanArgumentKind.Literal]),
        };

        return descriptors.ToDictionary(d => d.Operation);
    }
}
