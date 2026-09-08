namespace Sanare.Core.Repository;

/// <summary>Point-in-time state of the script repository. See docs/features/script-repository.md ("Interface").</summary>
public sealed record RepositoryStatus(
    bool IsInitialized,
    string DefaultBranch,
    bool IsWorkingTreeClean,
    string? HeadCommitId);
