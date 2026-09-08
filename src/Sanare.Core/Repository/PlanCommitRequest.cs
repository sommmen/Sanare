using Sanare.Abstractions.Plans;

namespace Sanare.Core.Repository;

/// <summary>
/// A request to commit an <see cref="ExtractionPlan"/> to the script repository. See
/// docs/features/script-repository.md ("Interfaces" → Inputs, "Commit and bootstrap behavior").
/// </summary>
/// <param name="Plan">The plan to commit; written as canonical JSON via <c>IPlanSerializer</c>.</param>
/// <param name="Verb">One of <c>author</c>, <c>heal</c>, <c>rollback</c>, <c>approve</c>.</param>
/// <param name="Summary">A short human-readable summary for the commit's first line.</param>
/// <param name="Reason">Free-form reason trailer, e.g. <c>authoring</c>, <c>heal:{degradationId}</c>.</param>
/// <param name="Branch">
/// The branch to commit on; <see langword="null"/> commits to the repository's configured default branch.
/// </param>
/// <param name="Approve">
/// When <see langword="true"/>, an approval tag is created for this commit in the same operation
/// (dev-mode auto-approve per <c>plan-resolver</c>'s approval-gating matrix).
/// </param>
public sealed record PlanCommitRequest(
    ExtractionPlan Plan,
    string Verb,
    string Summary,
    string Reason,
    string? Branch = null,
    bool Approve = false);
