namespace Sanare.Core.Repository;

/// <summary>
/// The difference between two commits, restricted to the <c>plans/</c> tree.
/// See docs/features/script-repository.md ("Implementation Plan" → T4).
/// </summary>
/// <param name="FromCommitId">The resolved id of the older side.</param>
/// <param name="ToCommitId">The resolved id of the newer side.</param>
/// <param name="Files">One entry per changed plan file.</param>
public sealed record PlanDiff(string FromCommitId, string ToCommitId, IReadOnlyList<PlanDiffFile> Files);

/// <summary>A single changed plan file within a <see cref="PlanDiff"/>.</summary>
/// <param name="Path">The repository-relative path, taken from the newer side unless the file was deleted.</param>
/// <param name="Status">How the file changed: <c>added</c>, <c>deleted</c>, <c>modified</c>, or <c>renamed</c>.</param>
/// <param name="LinesAdded">Added line count.</param>
/// <param name="LinesDeleted">Deleted line count.</param>
/// <param name="Patch">
/// The unified patch text, capped at <see cref="ScriptRepositoryOptions.MaxPlanSizeBytes"/> so a single
/// pathological diff cannot exhaust memory.
/// </param>
/// <param name="Truncated"><see langword="true"/> when <paramref name="Patch"/> was cut at the cap.</param>
public sealed record PlanDiffFile(
    string Path,
    string Status,
    int LinesAdded,
    int LinesDeleted,
    string Patch,
    bool Truncated);
