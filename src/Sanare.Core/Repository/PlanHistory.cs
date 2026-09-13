namespace Sanare.Core.Repository;

/// <summary>
/// The commit history of a single plan path, newest first. Renames are not followed: a plan's identity is
/// its <c>(sourceId, schemaName, schemaVersion)</c> triple, which maps to one stable path.
/// See docs/features/script-repository.md ("Implementation Plan" → T3).
/// </summary>
/// <param name="PlanPath">The repository-relative path whose history this is.</param>
/// <param name="Entries">Commits touching <paramref name="PlanPath"/>, newest first.</param>
public sealed record PlanHistory(string PlanPath, IReadOnlyList<PlanHistoryEntry> Entries);

/// <summary>One commit in a plan's history, with the approval tag pointing at it when there is one.</summary>
/// <param name="Commit">Provenance read back from the commit message's trailer block.</param>
/// <param name="Summary">The commit message's subject line.</param>
/// <param name="HasProvenance">
/// <see langword="false"/> when the commit carried no recognizable trailer block, in which case
/// <paramref name="Commit"/>'s provenance fields hold their defaults.
/// </param>
public sealed record PlanHistoryEntry(PlanCommitInfo Commit, string Summary, bool HasProvenance);
