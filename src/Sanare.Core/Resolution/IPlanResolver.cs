namespace Sanare.Core.Resolution;

/// <summary>
/// Resolves the approved <see cref="Sanare.Abstractions.Plans.ExtractionPlan"/> for a source/schema pair.
/// See docs/features/plan-resolver.md.
/// </summary>
/// <remarks>
/// This slice covers warm/cold resolution from the approval-tag index, numeric tag ordering, and schema-drift
/// detection. Single-flight authoring coordination, preview mode, degraded-plan diagnostics, and
/// preload-on-start are authoring-dependent or optimisation concerns and are out of scope for this slice
/// (no authoring workflow exists yet); a miss is always reported as <see cref="PlanResolutionFailure.NoPlanAvailable"/>.
/// </remarks>
public interface IPlanResolver
{
    ValueTask<PlanResolution> ResolveAsync(PlanResolutionRequest request, CancellationToken ct = default);

    /// <summary>Drops cached index entries for <paramref name="sourceId"/> (optionally scoped to one schema hash).</summary>
    void Invalidate(string sourceId, string? schemaHash = null);
}
