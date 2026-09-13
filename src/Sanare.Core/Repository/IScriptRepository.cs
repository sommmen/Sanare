using Sanare.Abstractions.Plans;

namespace Sanare.Core.Repository;

/// <summary>
/// Owns the on-disk git repository of extraction plans: bootstrap, reading a plan at a ref, committing new
/// plans, and approval tagging. See docs/features/script-repository.md.
/// </summary>
/// <remarks>
/// Blame, remotes, and merge/conflict resolution remain out of scope; see
/// docs/features/script-repository.md ("Deferred scope").
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

    /// <summary>
    /// Lists the commits touching one plan's path, newest first, each carrying the approval tag pointing at
    /// it when there is one (AC-GIT-009, AC-GIT-010). Renames are not followed. Takes no write lease.
    /// </summary>
    /// <param name="limit">Caps the number of entries returned; <see langword="null"/> returns all of them.</param>
    ValueTask<PlanHistory> GetHistoryAsync(
        string sourceId, string schemaName, int schemaVersion, int? limit = null, CancellationToken ct = default);

    /// <summary>
    /// Diffs two commits over the <c>plans/</c> tree (AC-GIT-011, AC-GIT-012). Either id failing to resolve
    /// raises <c>SNR-GIT-002</c>.
    /// </summary>
    ValueTask<PlanDiff> DiffAsync(string fromCommitId, string toCommitId, CancellationToken ct = default);

    /// <summary>
    /// Creates <c>heal/{source-id}/{yyyyMMdd}-{shortReason}</c> from the plan's currently approved commit,
    /// falling back to the default branch tip when nothing is approved yet (AC-GIT-013). Returns the branch
    /// name, which gains a numeric suffix on same-day collision.
    /// </summary>
    /// <param name="shortReason">
    /// Slugified into the branch name and rejected with <c>SNR-GIT-015</c> when it cannot be reduced to
    /// <c>^[a-z0-9-]+$</c>, so it can never inject a ref path.
    /// </param>
    ValueTask<string> CreateHealBranchAsync(
        string sourceId, string schemaName, int schemaVersion, string shortReason, CancellationToken ct = default);

    /// <summary>
    /// Fast-forwards the default branch to <paramref name="branch"/>'s tip and approves it (AC-GIT-014).
    /// A non-descendant tip raises <c>SNR-GIT-005</c> with every ref left untouched; a merge commit is never
    /// created. The returned <see cref="PlanCommitInfo.ApprovalTag"/> is what lets the caller invalidate
    /// <c>plan-resolver</c>'s warm index.
    /// </summary>
    ValueTask<PlanCommitInfo> PromoteAsync(
        string sourceId, string schemaName, int schemaVersion, string branch, CancellationToken ct = default);

    /// <summary>
    /// Re-points approval at an earlier commit by creating a new highest-numbered tag at
    /// <paramref name="targetCommitId"/> (AC-GIT-015, AC-GIT-016). Commits nothing and never deletes or moves
    /// a tag: the superseded plan becomes <c>Superseded</c>, never deleted. Idempotent when the target is
    /// already the approved commit.
    /// </summary>
    ValueTask<PlanCommitInfo> RollbackAsync(
        string sourceId, string schemaName, int schemaVersion, string targetCommitId, CancellationToken ct = default);

    /// <summary>
    /// Writes a diagnosis note to <c>notes/{source-id}/{yyyyMMddHHmmss}-{shortCommitId}.md</c> on the branch
    /// holding <paramref name="relatedCommitId"/> (AC-025). Notes never appear in
    /// <see cref="GetHistoryAsync"/>, which is restricted to <c>plans/</c>.
    /// </summary>
    ValueTask CommitDiagnosisNoteAsync(
        string sourceId, string markdown, string relatedCommitId, CancellationToken ct = default);

    /// <summary>
    /// Deletes <c>heal/*</c> branches that were promoted (tip is an ancestor of the default branch) or
    /// abandoned (older than <paramref name="olderThan"/> with no approval tag pointing into them), and
    /// returns their names (AC-GIT-017). Never deletes a branch whose tip carries a tag, never touches
    /// <c>plans/</c>, and never runs automatically.
    /// </summary>
    ValueTask<IReadOnlyList<string>> PruneHealBranchesAsync(TimeSpan olderThan, CancellationToken ct = default);
}

/// <summary>An approval tag and the commit id it points at.</summary>
public sealed record ApprovalTagEntry(string TagName, string CommitId);
