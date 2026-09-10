namespace Sanare.Core.Plans;

/// <summary>
/// A single structural problem found in an <see cref="Sanare.Abstractions.Plans.ExtractionPlan"/> by
/// <see cref="IPlanValidator"/>. See docs/features/extraction-plan-model.md ("Validation").
/// </summary>
/// <param name="PlanPointer">
/// A JSON-pointer-shaped path into the plan document naming where the defect was found, e.g.
/// <c>/consent/strategy</c> or <c>/fields/0/pointer</c>. Feeds straight back to the authoring agent and
/// to a human reading a failed CI run.
/// </param>
/// <param name="Code">
/// The diagnostic code the defect maps to — <c>SNR-PLAN-001</c> for every structural defect this
/// validator reports (docs/features/extraction-plan-model.md, "Error Handling").
/// </param>
/// <param name="Message">A human-readable explanation naming what was found and what was expected.</param>
public sealed record PlanDefect(string PlanPointer, string Code, string Message);
