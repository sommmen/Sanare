namespace Sanare.Core.Plans;

/// <summary>
/// Outcome of validating an <see cref="Sanare.Abstractions.Plans.ExtractionPlan"/> with
/// <see cref="IPlanValidator"/>. <see cref="IsValid"/> is <see langword="true"/> exactly when
/// <see cref="Defects"/> is empty — every defect is reported together rather than failing fast, so a
/// plan with several unrelated problems yields one result naming all of them (AC-PLAN-014).
/// </summary>
public sealed record PlanValidationResult(bool IsValid, IReadOnlyList<PlanDefect> Defects);
