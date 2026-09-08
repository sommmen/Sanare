using System.Collections.Concurrent;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;

namespace Sanare.Core.Resolution;

/// <summary>
/// Resolves approved plans from the script repository through a lock-free warm index keyed by
/// <c>(sourceId, schemaName, schemaVersion)</c>. See docs/features/plan-resolver.md.
/// </summary>
/// <remarks>
/// Authoring on a miss, single-flight coalescing, preview mode, and degraded-plan diagnostics are not
/// implemented here — there is no authoring workflow in this slice, so a miss always yields
/// <see cref="PlanResolutionFailure.NoPlanAvailable"/>. The approval-tag numeric-ordering and schema-drift
/// behaviours specified for <c>plan-resolver</c> are otherwise implemented as specified.
/// </remarks>
public sealed class PlanResolver(IScriptRepository repository, IPlanSerializer serializer) : IPlanResolver
{
    private readonly ConcurrentDictionary<PlanKey, ResolvedPlan> _index = new();

    public async ValueTask<PlanResolution> ResolveAsync(PlanResolutionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var key = new PlanKey(request.SourceId, request.SchemaName, request.SchemaVersion);

        if (_index.TryGetValue(key, out var cached))
        {
            return EvaluateSchemaHash(cached, request);
        }

        var tags = await repository.GetApprovalTagsAsync(request.SourceId, request.SchemaName, request.SchemaVersion, ct).ConfigureAwait(false);
        var prefix = ApprovalTagParser.BuildPrefix(request.SourceId, request.SchemaName, request.SchemaVersion);

        var highest = tags
            .Select(tag => (Tag: tag, Number: ApprovalTagParser.TryParseNumber(tag.TagName, prefix, out var number) ? number : (int?)null))
            .Where(entry => entry.Number is not null)
            .OrderByDescending(entry => entry.Number)
            .Select(entry => entry.Tag)
            .FirstOrDefault();

        if (highest is null)
        {
            return PlanResolution.Failed(PlanResolutionFailure.NoPlanAvailable, "SNR-PLAN-004", $"No approved plan exists for '{request.SourceId}'/{request.SchemaName}@{request.SchemaVersion}.");
        }

        var document = await repository.GetPlanAsync(request.SourceId, request.SchemaName, request.SchemaVersion, GitRef.Commit(highest.CommitId), ct).ConfigureAwait(false);
        if (document is null)
        {
            return PlanResolution.Failed(PlanResolutionFailure.NoPlanAvailable, "SNR-GIT-002", $"Approval tag '{highest.TagName}' points at a commit with no plan file.");
        }

        ExtractionPlan plan;
        try
        {
            plan = serializer.Read(document.Json);
        }
        catch (PlanSerializationException exception)
        {
            return PlanResolution.Failed(PlanResolutionFailure.PlanInvalid, "SNR-PLAN-001", exception.Message);
        }

        var resolved = new ResolvedPlan(plan, highest.CommitId, highest.TagName);
        _index[key] = resolved;
        return EvaluateSchemaHash(resolved, request);
    }

    public void Invalidate(string sourceId, string? schemaHash = null)
    {
        foreach (var key in _index.Keys)
        {
            if (!string.Equals(key.SourceId, sourceId, StringComparison.Ordinal))
            {
                continue;
            }

            if (schemaHash is not null && _index.TryGetValue(key, out var entry) && !string.Equals(entry.Plan.SchemaHash, schemaHash, StringComparison.Ordinal))
            {
                continue;
            }

            _index.TryRemove(key, out _);
        }
    }

    private static PlanResolution EvaluateSchemaHash(ResolvedPlan resolved, PlanResolutionRequest request)
    {
        if (!string.Equals(resolved.Plan.SchemaHash, request.SchemaHash, StringComparison.Ordinal))
        {
            return PlanResolution.Failed(
                PlanResolutionFailure.SchemaDrift,
                "SNR-PLAN-003",
                $"Plan schemaHash '{resolved.Plan.SchemaHash}' differs from the request's schemaHash '{request.SchemaHash}'.");
        }

        return PlanResolution.Resolved(resolved);
    }

    private readonly record struct PlanKey(string SourceId, string SchemaName, int SchemaVersion);
}
