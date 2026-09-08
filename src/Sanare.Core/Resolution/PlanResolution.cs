namespace Sanare.Core.Resolution;

/// <summary>
/// The result of <see cref="IPlanResolver.ResolveAsync"/>: either a resolved plan, or a typed failure with
/// an error code and message. See docs/features/plan-resolver.md ("Key Behaviors" → Interface).
/// </summary>
public readonly record struct PlanResolution(ResolvedPlan? Plan, PlanResolutionFailure? Failure, string? ErrorCode, string? Message)
{
    public bool IsResolved => Plan is not null;

    public static PlanResolution Resolved(ResolvedPlan plan) => new(plan, null, null, null);

    public static PlanResolution Failed(PlanResolutionFailure failure, string errorCode, string message) =>
        new(null, failure, errorCode, message);
}
