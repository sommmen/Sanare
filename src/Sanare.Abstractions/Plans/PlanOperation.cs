namespace Sanare.Abstractions.Plans;

/// <summary>
/// The closed, complete vocabulary of operations an <see cref="ExtractionPlan"/> may name. See
/// docs/features/extraction-plan-model.md ("The closed operation vocabulary") for full rules.
/// </summary>
/// <remarks>
/// This enum is the security boundary of the whole system (DR-001): a plan can only ever reference
/// operations from this fixed allow-list, so an LLM-authored plan cannot cause arbitrary code to run.
/// Deserialization of an unrecognised operation name is a hard failure (<c>SNR-PLAN-001</c>) — never a
/// skipped step, never a no-op. Adding a member here requires a <c>planVersion</c> bump plus a
/// corresponding entry in <see cref="PlanOperationCatalog"/> (enforced by a v0.1 unit test). Interpreting
/// these operations against real content is <c>plan-runtime</c> scope (M2) and is not implemented in
/// v0.1 — only the closed vocabulary and its static metadata are.
/// </remarks>
public enum PlanOperation
{
    // Locators
    SelectFirst,
    SelectAll,
    XPath,
    JsonPath,
    RegexCapture,
    Attribute,
    Text,
    Html,

    // Text transforms
    Trim,
    CollapseWhitespace,
    StripCurrency,
    StripUnit,

    // Parsers
    ParseInt,
    ParseDecimal,
    ParseBool,
    ParseDate,

    // Semantic transforms
    ResolveUrl,
    MapEnum,
    ConvertUnit,

    // Structural
    KeyValueTable,
    DefinitionList,
    Concat,
    Split,
    Index,
    Coalesce,
    Exists,
    NotFoundPredicate,

    // Browser-tier interactions
    Click,
    WaitForSelector,
    WaitForNetworkIdle,
    Scroll,
    SelectOption,
    Type,
}
