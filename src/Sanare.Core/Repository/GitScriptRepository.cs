using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using Sanare.Core.Plans;
using GitRepository = LibGit2Sharp.Repository;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Sanare.Core.Tests")]

namespace Sanare.Core.Repository;

/// <summary>
/// LibGit2Sharp-backed <see cref="IScriptRepository"/>. See docs/features/script-repository.md.
/// </summary>
/// <remarks>
/// Implements the minimal slice described on <see cref="IScriptRepository"/>: bootstrap, read-at-ref,
/// commit, and monotonic approval tagging. The <c>git</c>-CLI backend, heal branches, rollback-by-name,
/// history/diff, blame, and notes remain full <c>script-repository</c> scope.
/// </remarks>
public sealed class GitScriptRepository(
    ScriptRepositoryOptions options,
    IPlanSerializer serializer,
    IPlanValidator validator,
    IRepositoryCoordinator coordinator) : IScriptRepository
{
    private const string CommitAuthorTrailerPrefix = "Reason: ";

    private static readonly Regex SourceIdPattern = new("^[a-z0-9-]+(/[a-z0-9-]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SchemaNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*(?:[.-][A-Za-z0-9_]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly AsyncLocal<Action<GitRepository, string>?> ApprovalConflictSimulationHook = new();

    /// <summary>
    /// Test-only seam letting a test deterministically simulate the concurrent/external tag creation
    /// described for <c>SNR-GIT-006</c> (docs/features/script-repository.md), by inserting the
    /// computed next approval tag between its computation and this method's conflict check. Scoped via
    /// <see cref="AsyncLocal{T}"/> so setting it only affects the call flow that set it; it is a no-op,
    /// and therefore behavior-neutral, for every other caller.
    /// </summary>
    internal static Action<GitRepository, string>? ApprovalConflictSimulation
    {
        get => ApprovalConflictSimulationHook.Value;
        set => ApprovalConflictSimulationHook.Value = value;
    }

    public ValueTask InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(options.RepositoryPath);

        if (GitRepository.IsValid(options.RepositoryPath))
        {
            return ValueTask.CompletedTask;
        }

        GitRepository.Init(options.RepositoryPath);
        using var repo = new GitRepository(options.RepositoryPath);
        EnsureDefaultBranch(repo);

        File.WriteAllText(Path.Combine(options.RepositoryPath, ".gitattributes"), "*.plan.json text eol=lf\n*.md text eol=lf\n");
        File.WriteAllText(Path.Combine(options.RepositoryPath, ".gitignore"), ".sanare-lock\n*.tmp-*\n");

        Commands.Stage(repo, "*");
        var signature = BuildSignature();
        repo.Commit("chore: initialize plan repository", signature, signature, new CommitOptions());
        return ValueTask.CompletedTask;
    }

    public ValueTask<PlanDocument?> GetPlanAsync(
        string sourceId, string schemaName, int schemaVersion, GitRef? reference = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var repo = new GitRepository(options.RepositoryPath);
        var commit = ResolveCommit(repo, reference);
        if (commit is null)
        {
            return ValueTask.FromResult<PlanDocument?>(null);
        }

        var path = PlanPath(sourceId, schemaName, schemaVersion);
        var entry = commit[path];
        if (entry?.TargetType != TreeEntryTargetType.Blob)
        {
            return ValueTask.FromResult<PlanDocument?>(null);
        }

        var blob = (Blob)entry.Target;
        var json = blob.GetContentText(Encoding.UTF8);
        var document = new PlanDocument(json, commit.Sha, reference?.Value ?? options.DefaultBranch, commit.Author.When, commit.Message);
        return ValueTask.FromResult<PlanDocument?>(document);
    }

    public async ValueTask<PlanCommitInfo> CommitPlanAsync(PlanCommitRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var validation = validator.Validate(request.Plan);
        if (!validation.IsValid)
        {
            var details = string.Join("; ", validation.Defects.Select(defect =>
                $"{defect.PlanPointer}: {defect.Message}"));
            throw new ScriptRepositoryException("SNR-PLAN-001", $"Plan validation failed: {details}");
        }

        var json = serializer.WriteCanonical(request.Plan);
        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > ScriptRepositoryOptions.MaxPlanSizeBytes)
        {
            throw new ScriptRepositoryException("SNR-GIT-014", $"Plan document is {bytes} bytes, exceeding the {ScriptRepositoryOptions.MaxPlanSizeBytes}-byte limit.");
        }

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);

        using var repo = new GitRepository(options.RepositoryPath);
        var status = repo.RetrieveStatus();
        if (status.IsDirty)
        {
            throw new ScriptRepositoryException("SNR-GIT-003", "The script repository's working tree is dirty; refusing to commit over a human edit.");
        }

        var branchName = request.Branch ?? options.DefaultBranch;
        var branch = repo.Branches[branchName] ?? repo.CreateBranch(branchName, repo.Head.Tip ?? repo.Commits.FirstOrDefault());

        var relativePath = PlanPath(request.Plan.SourceId, request.Plan.SchemaName, request.Plan.SchemaVersion);
        var fullPath = Path.Combine(options.RepositoryPath, relativePath);
        var parentDir = Path.GetDirectoryName(fullPath)!;
        var parentDirExists = Directory.Exists(parentDir);
        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(parentDir);

        try
        {
            await File.WriteAllTextAsync(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }

            throw;
        }

        var moveSucceeded = false;
        var commitSucceeded = false;
        try
        {
            File.Move(tempPath, fullPath, overwrite: true);
            moveSucceeded = true;
            Commands.Stage(repo, relativePath);
            var message = BuildCommitMessage(request);
            var signature = BuildSignature();

            repo.Refs.UpdateTarget("HEAD", $"refs/heads/{branchName}");

            try
            {
                var commit = repo.Commit(message, signature, signature, new CommitOptions());
                commitSucceeded = true;

                if (branch.Tip is null || request.Branch is null)
                {
                    repo.Refs.UpdateTarget(branch.Reference, commit.Id);
                }

                var info = new PlanCommitInfo(
                    commit.Sha,
                    branchName,
                    null,
                    request.Plan.SchemaName,
                    request.Plan.SchemaVersion,
                    request.Plan.SchemaHash,
                    request.Plan.Provenance.Model,
                    request.Plan.Provenance.Attempts,
                    request.Plan.Provenance.FixtureIds,
                    request.Plan.Provenance.Score,
                    request.Reason,
                    commit.Author.When);

                if (request.Approve)
                {
                    return await ApproveInternalAsync(repo, request.Plan.SourceId, request.Plan.SchemaName, request.Plan.SchemaVersion, commit.Sha, info, ct).ConfigureAwait(false);
                }

                return info;
            }
            catch
            {
                if (!commitSucceeded && moveSucceeded)
                {
                    try
                    {
                        Commands.Unstage(repo, relativePath);
                    }
                    catch
                    {
                    }

                    if (File.Exists(fullPath))
                    {
                        try
                        {
                            File.Delete(fullPath);
                        }
                        catch
                        {
                        }
                    }

                    if (!parentDirExists && Directory.Exists(parentDir))
                    {
                        try
                        {
                            Directory.Delete(parentDir, recursive: true);
                        }
                        catch
                        {
                        }
                    }
                }

                throw;
            }
        }
        catch
        {
            if (!moveSucceeded)
            {
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch
                    {
                    }
                }
            }
            else if (!commitSucceeded)
            {
                try
                {
                    Commands.Unstage(repo, relativePath);
                }
                catch
                {
                }

                if (File.Exists(fullPath))
                {
                    try
                    {
                        File.Delete(fullPath);
                    }
                    catch
                    {
                    }
                }

                if (!parentDirExists && Directory.Exists(parentDir))
                {
                    try
                    {
                        Directory.Delete(parentDir, recursive: true);
                    }
                    catch
                    {
                    }
                }
            }

            throw;
        }
    }

    public async ValueTask<PlanCommitInfo> ApproveAsync(
        string sourceId, string schemaName, int schemaVersion, string commitId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);
        var commit = repo.Lookup<Commit>(commitId) ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{commitId}' was not found.");
        var info = new PlanCommitInfo(commit.Sha, options.DefaultBranch, null, schemaName, schemaVersion, string.Empty, string.Empty, 0, [], 0d, "approval", commit.Author.When);
        return await ApproveInternalAsync(repo, sourceId, schemaName, schemaVersion, commit.Sha, info, ct).ConfigureAwait(false);
    }

    public ValueTask<RepositoryStatus> GetStatusAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!GitRepository.IsValid(options.RepositoryPath))
        {
            return ValueTask.FromResult(new RepositoryStatus(false, options.DefaultBranch, true, null));
        }

        using var repo = new GitRepository(options.RepositoryPath);
        var status = repo.RetrieveStatus();
        return ValueTask.FromResult(new RepositoryStatus(true, options.DefaultBranch, !status.IsDirty, repo.Head.Tip?.Sha));
    }

    public ValueTask<IReadOnlyList<ApprovalTagEntry>> GetApprovalTagsAsync(
        string sourceId, string schemaName, int schemaVersion, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateIdentifiers(sourceId, schemaName);

        if (!GitRepository.IsValid(options.RepositoryPath))
        {
            return ValueTask.FromResult<IReadOnlyList<ApprovalTagEntry>>([]);
        }

        var prefix = $"approved/{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}/";
        using var repo = new GitRepository(options.RepositoryPath);
        var entries = repo.Tags
            .Where(tag => tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal))
            .Select(tag => new ApprovalTagEntry(tag.FriendlyName, PeelToCommit(tag)?.Sha ?? tag.Target.Sha))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ApprovalTagEntry>>(entries);
    }

    private ValueTask<PlanCommitInfo> ApproveInternalAsync(
        GitRepository repo, string sourceId, string schemaName, int schemaVersion, string commitId, PlanCommitInfo fallback, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ValidateIdentifiers(sourceId, schemaName);

        var prefix = $"approved/{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}/";
        var existingTags = repo.Tags.Where(tag => tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

        var highest = existingTags
            .Select(tag => (Tag: tag, Number: ParseTagNumber(tag.FriendlyName, prefix)))
            .Where(entry => entry.Number is not null)
            .OrderByDescending(entry => entry.Number)
            .FirstOrDefault();

        if (highest.Tag is not null && string.Equals(PeelToCommit(highest.Tag)?.Sha, commitId, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(fallback with { ApprovalTag = highest.Tag.FriendlyName });
        }

        var nextNumber = (highest.Number ?? 0) + 1;
        var tagName = prefix + nextNumber.ToString(CultureInfo.InvariantCulture);

        ApprovalConflictSimulation?.Invoke(repo, tagName);

        if (repo.Tags[tagName] is not null)
        {
            throw new ScriptRepositoryException("SNR-GIT-006", $"Approval tag '{tagName}' already points at a different commit.");
        }

        var commit = repo.Lookup<Commit>(commitId) ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{commitId}' was not found.");
        repo.Tags.Add(tagName, commit);
        return ValueTask.FromResult(fallback with { ApprovalTag = tagName });
    }

    private static int? ParseTagNumber(string tagFriendlyName, string prefix)
    {
        var suffix = tagFriendlyName[prefix.Length..];
        return int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    private Commit? ResolveCommit(GitRepository repo, GitRef? reference)
    {
        if (reference is null)
        {
            return repo.Branches[options.DefaultBranch]?.Tip;
        }

        var direct = repo.Lookup<Commit>(reference.Value);
        if (direct is not null)
        {
            return direct;
        }

        var tag = repo.Tags[reference.Value];
        return (repo.Branches[reference.Value]?.Tip) ?? (tag is not null ? PeelToCommit(tag) : null);
    }

    private void EnsureDefaultBranch(GitRepository repo)
    {
        if (repo.Head.FriendlyName != options.DefaultBranch)
        {
            repo.Refs.UpdateTarget("HEAD", $"refs/heads/{options.DefaultBranch}");
        }
    }

    private Signature BuildSignature() => new(options.CommitterName, options.CommitterEmail, DateTimeOffset.UtcNow);

    private static Commit? PeelToCommit(Tag tag)
    {
        // For annotated tags, Tag.Target is a TagAnnotation object (not a Commit).
        // Tag.PeeledTarget returns the final non-tag object regardless of annotation.
        // For lightweight tags, PeeledTarget is the direct Commit.
        return tag.PeeledTarget as Commit;
    }

    private static void ValidateIdentifiers(string sourceId, string schemaName)
    {
        if (string.IsNullOrEmpty(sourceId))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", "sourceId must not be null or empty.");
        }

        if (string.IsNullOrEmpty(schemaName))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", "schemaName must not be null or empty.");
        }

        if (!SourceIdPattern.IsMatch(sourceId))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", $"sourceId '{sourceId}' contains invalid characters or format; expected slash-separated lowercase alphanumerics and hyphens.");
        }

        if (!SchemaNamePattern.IsMatch(schemaName))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", $"schemaName '{schemaName}' contains invalid characters or format; expected CLR type name format (letters/digits/underscore and optional hyphens or dots).");
        }
    }

    private static string PlanPath(string sourceId, string schemaName, int schemaVersion)
    {
        ValidateIdentifiers(sourceId, schemaName);
        return $"plans/{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}.plan.json";
    }

    private static string BuildCommitMessage(PlanCommitRequest request)
    {
        var plan = request.Plan;
        var builder = new StringBuilder();
        builder.Append(request.Verb).Append('(').Append(plan.SourceId).Append('/').Append(plan.SchemaName).Append("): ").Append(request.Summary).Append('\n');
        builder.Append('\n');
        builder.Append("Schema: ").Append(plan.SchemaName).Append('@').Append(plan.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append(" (").Append(plan.SchemaHash).Append(")\n");
        builder.Append("Tier: ").Append(plan.Tier).Append('\n');
        builder.Append("Plan-Version: ").Append(plan.PlanVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("Score: ").Append(plan.Provenance.Score.ToString("0.0000", CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("Fixtures: ").Append(string.Join(", ", plan.Provenance.FixtureIds)).Append('\n');
        builder.Append("Model: ").Append(plan.Provenance.Model).Append('\n');
        builder.Append("Attempts: ").Append(plan.Provenance.Attempts.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append(CommitAuthorTrailerPrefix).Append(request.Reason).Append('\n');
        return builder.ToString();
    }
}
