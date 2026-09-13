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
/// Blame, remotes, and merge/conflict resolution remain out of scope; see
/// docs/features/script-repository.md ("Deferred scope").
/// </remarks>
public sealed class GitScriptRepository(
    ScriptRepositoryOptions options,
    IPlanSerializer serializer,
    IPlanValidator validator,
    IRepositoryCoordinator coordinator) : IScriptRepository
{

    private const string PlansTreePrefix = "plans/";
    private const string HealBranchPrefix = "heal/";

    private static readonly Regex SourceIdPattern = new("^[a-z0-9-]+(/[a-z0-9-]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SchemaNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*(?:[.-][A-Za-z0-9_]+)*$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex HealReasonPattern = new("^[a-z0-9-]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
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

    public ValueTask<PlanHistory> GetHistoryAsync(
        string sourceId, string schemaName, int schemaVersion, int? limit = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var path = PlanPath(sourceId, schemaName, schemaVersion);

        if (limit is <= 0)
        {
            throw new ScriptRepositoryException("SNR-GIT-015", "limit must be greater than zero when specified.");
        }

        if (!GitRepository.IsValid(options.RepositoryPath))
        {
            return ValueTask.FromResult(new PlanHistory(path, []));
        }

        // Reads take no write lease, matching GetPlanAsync.
        using var repo = new GitRepository(options.RepositoryPath);

        var tagsByCommit = ApprovalTagPrefix(sourceId, schemaName, schemaVersion) is var prefix
            ? repo.Tags
                .Where(tag => tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal))
                .GroupBy(tag => PeelToCommit(tag)?.Sha)
                .Where(group => group.Key is not null)
                .ToDictionary(group => group.Key!, group => group.OrderByDescending(tag => ParseTagNumber(tag.FriendlyName, prefix)).First().FriendlyName, StringComparer.Ordinal)
            : [];

        var filter = new CommitFilter { SortBy = CommitSortStrategies.Topological | CommitSortStrategies.Time };
        var entries = new List<PlanHistoryEntry>();

        foreach (var commit in repo.Commits.QueryBy(path, filter))
        {
            ct.ThrowIfCancellationRequested();

            var sha = commit.Commit.Sha;
            var hasProvenance = PlanCommitMessage.TryParse(
                commit.Commit.Message,
                sha,
                options.DefaultBranch,
                tagsByCommit.GetValueOrDefault(sha),
                commit.Commit.Author.When,
                out var info);

            entries.Add(new PlanHistoryEntry(info, commit.Commit.MessageShort, hasProvenance));

            if (limit is { } max && entries.Count >= max)
            {
                break;
            }
        }

        return ValueTask.FromResult(new PlanHistory(path, entries));
    }

    public ValueTask<PlanDiff> DiffAsync(string fromCommitId, string toCommitId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrEmpty(fromCommitId);
        ArgumentException.ThrowIfNullOrEmpty(toCommitId);

        if (!GitRepository.IsValid(options.RepositoryPath))
        {
            throw new ScriptRepositoryException("SNR-GIT-001", $"No git repository exists at '{options.RepositoryPath}'.");
        }

        using var repo = new GitRepository(options.RepositoryPath);

        var from = ResolveCommit(repo, GitRef.Commit(fromCommitId))
            ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{fromCommitId}' was not found.");
        var to = ResolveCommit(repo, GitRef.Commit(toCommitId))
            ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{toCommitId}' was not found.");

        var patch = repo.Diff.Compare<Patch>(from.Tree, to.Tree, [PlansTreePrefix]);

        var files = new List<PlanDiffFile>();
        foreach (var change in patch)
        {
            ct.ThrowIfCancellationRequested();

            var path = change.Status == ChangeKind.Deleted ? change.OldPath : change.Path;
            if (!path.StartsWith(PlansTreePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var text = change.Patch ?? string.Empty;
            var truncated = Encoding.UTF8.GetByteCount(text) > ScriptRepositoryOptions.MaxPlanSizeBytes;
            if (truncated)
            {
                text = TruncateToBytes(text, ScriptRepositoryOptions.MaxPlanSizeBytes);
            }

            files.Add(new PlanDiffFile(
                path,
                change.Status.ToString().ToLowerInvariant(),
                change.LinesAdded,
                change.LinesDeleted,
                text,
                truncated));
        }

        return ValueTask.FromResult(new PlanDiff(from.Sha, to.Sha, files));
    }

    public async ValueTask<string> CreateHealBranchAsync(
        string sourceId, string schemaName, int schemaVersion, string shortReason, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateIdentifiers(sourceId, schemaName);
        var slug = Slugify(shortReason);

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);

        // Branch from the currently approved commit so healing never inherits unapproved work sitting on HEAD.
        var startPoint = FindApprovedCommit(repo, sourceId, schemaName, schemaVersion)
            ?? repo.Branches[options.DefaultBranch]?.Tip
            ?? throw new ScriptRepositoryException("SNR-GIT-002", "The repository has no commits to branch from.");

        var stem = $"heal/{sourceId}/{DateTimeOffset.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{slug}";
        var name = stem;
        for (var suffix = 2; repo.Branches[name] is not null; suffix++)
        {
            name = $"{stem}-{suffix.ToString(CultureInfo.InvariantCulture)}";
        }

        repo.CreateBranch(name, startPoint);
        return name;
    }

    public async ValueTask<PlanCommitInfo> PromoteAsync(
        string sourceId, string schemaName, int schemaVersion, string branch, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateIdentifiers(sourceId, schemaName);
        ArgumentException.ThrowIfNullOrEmpty(branch);

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);

        if (repo.RetrieveStatus().IsDirty)
        {
            throw new ScriptRepositoryException("SNR-GIT-003", "The script repository's working tree is dirty; refusing to promote over a human edit.");
        }

        var source = repo.Branches[branch] ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Branch '{branch}' was not found.");
        var tip = source.Tip ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Branch '{branch}' has no commits.");

        var target = repo.Branches[options.DefaultBranch] ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Branch '{options.DefaultBranch}' was not found.");
        var targetTip = target.Tip ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Branch '{options.DefaultBranch}' has no commits.");

        // Fast-forward only: a non-descendant tip means the default branch moved on, and merging is out of scope.
        if (!string.Equals(tip.Sha, targetTip.Sha, StringComparison.Ordinal)
            && !string.Equals(repo.ObjectDatabase.FindMergeBase(tip, targetTip)?.Sha, targetTip.Sha, StringComparison.Ordinal))
        {
            throw new ScriptRepositoryException("SNR-GIT-005",
                $"Branch '{branch}' has diverged from '{options.DefaultBranch}'; promotion would require a merge.");
        }

        repo.Refs.UpdateTarget(target.Reference, tip.Id);

        var fallback = BuildCommitInfo(repo, tip, options.DefaultBranch);
        return await ApproveInternalAsync(repo, sourceId, schemaName, schemaVersion, tip.Sha, fallback, ct).ConfigureAwait(false);
    }

    public async ValueTask<PlanCommitInfo> RollbackAsync(
        string sourceId, string schemaName, int schemaVersion, string targetCommitId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidateIdentifiers(sourceId, schemaName);
        ArgumentException.ThrowIfNullOrEmpty(targetCommitId);

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);

        var target = repo.Lookup<Commit>(targetCommitId)
            ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{targetCommitId}' was not found.");

        var approved = FindApprovedCommit(repo, sourceId, schemaName, schemaVersion)
            ?? throw new ScriptRepositoryException("SNR-GIT-005",
                $"No approved commit exists for '{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}'; there is nothing to roll back from.");

        if (!string.Equals(target.Sha, approved.Sha, StringComparison.Ordinal)
            && !string.Equals(repo.ObjectDatabase.FindMergeBase(target, approved)?.Sha, target.Sha, StringComparison.Ordinal))
        {
            throw new ScriptRepositoryException("SNR-GIT-005",
                $"Commit '{targetCommitId}' is not an ancestor of the currently approved commit; rollback only moves approval backwards.");
        }

        var path = PlanPath(sourceId, schemaName, schemaVersion);
        if (target[path]?.TargetType != TreeEntryTargetType.Blob)
        {
            throw new ScriptRepositoryException("SNR-GIT-002", $"Plan '{path}' does not exist at commit '{targetCommitId}'.");
        }

        // Approval re-points forward to a new highest-numbered tag; the superseded tag is never deleted.
        var fallback = BuildCommitInfo(repo, target, options.DefaultBranch);
        return await ApproveInternalAsync(repo, sourceId, schemaName, schemaVersion, target.Sha, fallback, ct).ConfigureAwait(false);
    }

    public async ValueTask CommitDiagnosisNoteAsync(
        string sourceId, string markdown, string relatedCommitId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentException.ThrowIfNullOrEmpty(relatedCommitId);

        if (string.IsNullOrEmpty(sourceId) || !SourceIdPattern.IsMatch(sourceId))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", $"sourceId '{sourceId}' does not match {SourceIdPattern}.");
        }

        var bytes = Encoding.UTF8.GetByteCount(markdown);
        if (bytes > ScriptRepositoryOptions.MaxPlanSizeBytes)
        {
            throw new ScriptRepositoryException("SNR-GIT-014", $"Diagnosis note is {bytes} bytes, exceeding the {ScriptRepositoryOptions.MaxPlanSizeBytes}-byte limit.");
        }

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);

        if (repo.RetrieveStatus().IsDirty)
        {
            throw new ScriptRepositoryException("SNR-GIT-003", "The script repository's working tree is dirty; refusing to commit over a human edit.");
        }

        var related = repo.Lookup<Commit>(relatedCommitId)
            ?? throw new ScriptRepositoryException("SNR-GIT-002", $"Commit '{relatedCommitId}' was not found.");

        // Notes land on whichever branch holds the commit they diagnose, so a heal branch keeps its own record.
        var branchName = repo.Branches.FirstOrDefault(b => !b.IsRemote && string.Equals(b.Tip?.Sha, related.Sha, StringComparison.Ordinal))?.FriendlyName
            ?? repo.Branches.FirstOrDefault(b => !b.IsRemote && b.Commits.Any(c => string.Equals(c.Sha, related.Sha, StringComparison.Ordinal)))?.FriendlyName
            ?? options.DefaultBranch;

        var branch = repo.Branches[branchName]!;
        var relativePath = $"notes/{sourceId}/{DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}-{related.Sha[..7]}.md";
        var fullPath = Path.Combine(options.RepositoryPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        await File.WriteAllTextAsync(tempPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct).ConfigureAwait(false);

        try
        {
            File.Move(tempPath, fullPath, overwrite: true);
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

        Commands.Stage(repo, relativePath);
        var signature = BuildSignature();
        repo.Refs.UpdateTarget("HEAD", $"refs/heads/{branchName}");
        var commit = repo.Commit($"docs({sourceId}): record diagnosis note\n", signature, signature, new CommitOptions());

        if (branch.Tip is null || !string.Equals(branch.Tip.Sha, commit.Sha, StringComparison.Ordinal))
        {
            repo.Refs.UpdateTarget(branch.Reference, commit.Id);
        }
    }

    public async ValueTask<IReadOnlyList<string>> PruneHealBranchesAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (olderThan < TimeSpan.Zero)
        {
            throw new ScriptRepositoryException("SNR-GIT-015", "olderThan must not be negative.");
        }

        await using var lease = await coordinator.AcquireWriteLeaseAsync(options.EffectiveLockTimeout, ct).ConfigureAwait(false);
        using var repo = new GitRepository(options.RepositoryPath);

        var defaultTip = repo.Branches[options.DefaultBranch]?.Tip;
        var taggedCommits = repo.Tags
            .Select(tag => PeelToCommit(tag)?.Sha)
            .Where(sha => sha is not null)
            .ToHashSet(StringComparer.Ordinal);

        var cutoff = DateTimeOffset.UtcNow - olderThan;
        var deleted = new List<string>();

        foreach (var branch in repo.Branches.Where(b => !b.IsRemote && b.FriendlyName.StartsWith(HealBranchPrefix, StringComparison.Ordinal)).ToArray())
        {
            ct.ThrowIfCancellationRequested();

            var tip = branch.Tip;
            if (tip is null || string.Equals(branch.FriendlyName, repo.Head.FriendlyName, StringComparison.Ordinal))
            {
                continue;
            }

            // A promoted branch is safe to delete at any age: its tip is reachable from the default branch,
            // so neither the commits nor any tag on them become unreachable.
            var promoted = defaultTip is not null
                && string.Equals(repo.ObjectDatabase.FindMergeBase(tip, defaultTip)?.Sha, tip.Sha, StringComparison.Ordinal);

            if (!promoted)
            {
                // An unpromoted tagged tip is approved history the default branch cannot reach; deleting the
                // branch would strand it, so it is never garbage regardless of age.
                if (taggedCommits.Contains(tip.Sha) || tip.Author.When >= cutoff)
                {
                    continue;
                }
            }

            repo.Branches.Remove(branch);
            deleted.Add(branch.FriendlyName);
        }

        return deleted;
    }

    private string ApprovalTagPrefix(string sourceId, string schemaName, int schemaVersion) =>
        $"approved/{sourceId}/{schemaName}@{schemaVersion.ToString(CultureInfo.InvariantCulture)}/";

    private Commit? FindApprovedCommit(GitRepository repo, string sourceId, string schemaName, int schemaVersion)
    {
        var prefix = ApprovalTagPrefix(sourceId, schemaName, schemaVersion);
        return repo.Tags
            .Where(tag => tag.FriendlyName.StartsWith(prefix, StringComparison.Ordinal))
            .Select(tag => (Tag: tag, Number: ParseTagNumber(tag.FriendlyName, prefix)))
            .Where(entry => entry.Number is not null)
            .OrderByDescending(entry => entry.Number)
            .Select(entry => PeelToCommit(entry.Tag))
            .FirstOrDefault();
    }

    private PlanCommitInfo BuildCommitInfo(GitRepository repo, Commit commit, string branch)
    {
        _ = repo;
        _ = PlanCommitMessage.TryParse(commit.Message, commit.Sha, branch, null, commit.Author.When, out var info);
        return info;
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ScriptRepositoryException("SNR-GIT-015", "shortReason must not be null or whitespace.");
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch))
            {
                builder.Append(ch);
            }
            else if (ch is ' ' or '_' or '-')
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }
            }
            else
            {
                // Anything else — path separators, dots, control characters — is rejected rather than
                // silently rewritten, so "../../etc/passwd" can never be laundered into a valid slug.
                throw new ScriptRepositoryException("SNR-GIT-015",
                    $"shortReason '{value}' contains '{ch}', which is not permitted in a branch name segment.");
            }
        }

        var slug = builder.ToString().Trim('-');
        if (slug.Length == 0 || !HealReasonPattern.IsMatch(slug))
        {
            throw new ScriptRepositoryException("SNR-GIT-015",
                $"shortReason '{value}' cannot be reduced to a branch-safe slug matching {HealReasonPattern}.");
        }

        return slug;
    }

    private static string TruncateToBytes(string text, int maxBytes)
    {
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var buffer = encoding.GetBytes(text);
        if (buffer.Length <= maxBytes)
        {
            return text;
        }

        // Trim back to a whole UTF-8 sequence so the cap never splits a character.
        var length = maxBytes;
        while (length > 0 && (buffer[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return encoding.GetString(buffer, 0, length);
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

    private static string BuildCommitMessage(PlanCommitRequest request) => PlanCommitMessage.Build(request);
}
