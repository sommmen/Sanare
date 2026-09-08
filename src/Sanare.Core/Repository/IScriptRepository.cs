using Sanare.Abstractions.Plans;

namespace Sanare.Core.Repository;

/// <summary>
/// Owns the on-disk git repository of extraction plans: bootstrap, reading a plan at a ref, committing new
/// plans, and approval tagging. See docs/features/script-repository.md.
/// </summary>
/// <remarks>
/// This is the minimal slice needed by <c>plan-resolver</c> to serve persisted plans: heal branches,
/// rollback-by-name, history, diff, blame, and notes are out of scope for this slice and remain
/// <c>script-repository</c>'s full backlog.
/// </remarks>
public interface IScriptRepository
{
    /// <summary>
    /// Creates the repository at <c>{StateRoot}/scripts/</c> if absent (scaffold commit, <c>.gitattributes</c>,
    /// identity); a no-op if it already exists (AC-GIT-001, AC-GIT-002).
    /// </summary>
    ValueTask InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the plan document for <paramref name="sourceId"/>/<paramref name="schemaName"/>@<paramref name="schemaVersion"/>
    /// at <paramref name="reference"/>, or at the default branch's <c>HEAD</c> when <see langword="null"/>.
    /// Returns <see langword="null"/> when the plan file does not exist at that ref.
    /// </summary>
    ValueTask<PlanDocument?> GetPlanAsync(
        string sourceId, string schemaName, int schemaVersion, GitRef? reference = null, CancellationToken ct = default);

    /// <summary>
    /// Commits <paramref name="request"/>'s plan as canonical JSON. Fails <c>SNR-GIT-003</c> on a dirty
    /// working tree, <c>SNR-GIT-004</c> on a lease timeout.
    /// </summary>
    ValueTask<PlanCommitInfo> CommitPlanAsync(PlanCommitRequest request, CancellationToken ct = default);

    /// <summary>
    /// Creates the next monotonic <c>approved/{source-id}/{schema-name}@{schemaVersion}/{n}</c> tag pointing
    /// at <paramref name="commitId"/>. Idempotent when the same commit is approved twice (AC-GIT-008); fails
    /// <c>SNR-GIT-006</c> when a different commit is approved under an existing tag name.
    /// </summary>
    ValueTask<PlanCommitInfo> ApproveAsync(
        string sourceId, string schemaName, int schemaVersion, string commitId, CancellationToken ct = default);

    /// <summary>Reports the repository's current state (initialized, default branch, clean/dirty, HEAD).</summary>
    ValueTask<RepositoryStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Enumerates approval tags in the <c>approved/{source-id}/{schema-name}@{schemaVersion}/*</c> namespace,
    /// used by <c>plan-resolver</c> to build its warm index. See docs/features/plan-resolver.md
    /// ("Warm index and tag resolution").
    /// </summary>
    ValueTask<IReadOnlyList<ApprovalTagEntry>> GetApprovalTagsAsync(
        string sourceId, string schemaName, int schemaVersion, CancellationToken ct = default);
}

/// <summary>An approval tag and the commit id it points at.</summary>
public sealed record ApprovalTagEntry(string TagName, string CommitId);
