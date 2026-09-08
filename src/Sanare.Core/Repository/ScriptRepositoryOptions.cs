namespace Sanare.Core.Repository;

/// <summary>
/// Configuration for <see cref="GitScriptRepository"/>. See docs/features/script-repository.md
/// ("Interfaces" → Inputs) and docs/features/hosting-configuration.md.
/// </summary>
/// <param name="StateRoot">The root directory under which <c>scripts/</c> is created.</param>
/// <param name="DefaultBranch">The repository's default branch name.</param>
/// <param name="CommitterName">Committer identity, configured at the repository level only.</param>
/// <param name="CommitterEmail">Committer identity, configured at the repository level only.</param>
/// <param name="LockTimeout">How long <see cref="FileLockRepositoryCoordinator"/> retries before failing <c>SNR-GIT-004</c>.</param>
public sealed record ScriptRepositoryOptions(
    string StateRoot,
    string DefaultBranch = "main",
    string CommitterName = "sanare",
    string CommitterEmail = "scraper@localhost",
    TimeSpan? LockTimeout = null)
{
    /// <summary>The repository's working directory: <c>{StateRoot}/scripts</c>.</summary>
    public string RepositoryPath => Path.Combine(StateRoot, "scripts");

    /// <summary>Effective lock-acquisition timeout, defaulting to 30 seconds per the spec.</summary>
    public TimeSpan EffectiveLockTimeout => LockTimeout ?? TimeSpan.FromSeconds(30);

    /// <summary>Maximum plan document size in bytes before a commit is rejected (§7.3, 512 KB).</summary>
    public const int MaxPlanSizeBytes = 512 * 1024;
}
