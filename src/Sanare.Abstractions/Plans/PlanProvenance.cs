namespace Sanare.Abstractions.Plans;

/// <summary>
/// Records how an <see cref="ExtractionPlan"/> came to exist: who/what authored it, how many attempts it
/// took, which fixtures it was validated against, and the authoring evaluator's confidence score. See
/// docs/features/extraction-plan-model.md ("Object model").
/// </summary>
/// <param name="AuthoredBy">Identifies the authoring actor, e.g. <c>"agent"</c> or a human operator id.</param>
/// <param name="Model">The LLM (or tool) identifier used during authoring, when applicable.</param>
/// <param name="Attempts">How many authoring/repair attempts were made before this plan was produced.</param>
/// <param name="FixtureIds">The fixture corpus entries this plan was validated against during authoring.</param>
/// <param name="Score">The authoring evaluator's confidence score for this plan, in <c>[0, 1]</c>.</param>
/// <param name="AuthoredAt">When this plan was produced.</param>
/// <remarks>
/// v0.1 only defines the shape; populating it is <c>authoring-workflow</c>/<c>agents</c> scope (post-M1).
/// A hand-written v0.1 fixture plan may set placeholder provenance values.
/// </remarks>
public sealed record PlanProvenance(
    string AuthoredBy,
    string Model,
    int Attempts,
    IReadOnlyList<string> FixtureIds,
    double Score,
    DateTimeOffset AuthoredAt);
