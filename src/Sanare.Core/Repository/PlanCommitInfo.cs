namespace Sanare.Core.Repository;

/// <summary>
/// Provenance of a plan commit, parsed back from the commit message's structured trailer block. See
/// docs/features/script-repository.md ("Commit and bootstrap behavior").
/// </summary>
public sealed record PlanCommitInfo(
    string CommitId,
    string Branch,
    string? ApprovalTag,
    string SchemaName,
    int SchemaVersion,
    string SchemaHash,
    string Model,
    int Attempts,
    IReadOnlyList<string> FixtureIds,
    double Score,
    string Reason,
    DateTimeOffset CommittedAt);
