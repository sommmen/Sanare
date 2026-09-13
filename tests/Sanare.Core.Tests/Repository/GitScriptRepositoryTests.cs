using LibGit2Sharp;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

public sealed class GitScriptRepositoryTests : IDisposable
{
    private readonly List<ScriptRepositoryFixture> _fixtures = [];

    [Fact]
    public async Task InitializeAsync_creates_repository_and_is_idempotent()
    {
        var repository = CreateRepository();

        await repository.InitializeAsync();
        var statusAfterFirst = await repository.GetStatusAsync();
        await repository.InitializeAsync();
        var statusAfterSecond = await repository.GetStatusAsync();

        Assert.True(statusAfterFirst.IsInitialized);
        Assert.True(statusAfterFirst.IsWorkingTreeClean);
        Assert.Equal(statusAfterFirst.HeadCommitId, statusAfterSecond.HeadCommitId);
    }

    [Fact]
    public async Task CommitPlanAsync_then_GetPlanAsync_round_trips_the_plan()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan();

        var commitInfo = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "initial plan", "authoring"));
        var document = await repository.GetPlanAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);

        Assert.NotNull(document);
        Assert.Equal(commitInfo.CommitId, document!.CommitId);
        var roundTripped = new PlanSerializer().Read(document.Json);
        Assert.Equal(document.Json, new PlanSerializer().WriteCanonical(roundTripped));
    }

    [Fact]
    public async Task CommitPlanAsync_rejects_structurally_invalid_plans_before_writing()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var invalidPlan = SamplePlan() with { SchemaHash = string.Empty };

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(() =>
            repository.CommitPlanAsync(new PlanCommitRequest(invalidPlan, "author", "invalid plan", "authoring")).AsTask());

        Assert.Equal("SNR-PLAN-001", exception.Code);
        Assert.Contains("/schemaHash", exception.Message, StringComparison.Ordinal);
        Assert.Null(await repository.GetPlanAsync(invalidPlan.SourceId, invalidPlan.SchemaName, invalidPlan.SchemaVersion));
    }

    [Fact]
    public async Task GetPlanAsync_returns_null_when_plan_does_not_exist()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        var document = await repository.GetPlanAsync("unknown/source", "Missing", 1);

        Assert.Null(document);
    }

    [Fact]
    public async Task CommitPlanAsync_throws_SNR_GIT_003_when_working_tree_is_dirty()
    {
        var repository = CreateRepository(out var options);
        await repository.InitializeAsync();
        File.WriteAllText(Path.Combine(options.RepositoryPath, "stray-edit.txt"), "uncommitted");

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitPlanAsync(new PlanCommitRequest(SamplePlan(), "author", "should fail", "authoring")).AsTask());

        Assert.Equal("SNR-GIT-003", exception.Code);
    }

    [Fact]
    public async Task CommitPlanAsync_throws_retryable_SNR_GIT_004_when_the_write_lease_times_out()
    {
        var fixture = new ScriptRepositoryFixture(lockTimeout: TimeSpan.FromMilliseconds(100));
        _fixtures.Add(fixture);
        var repository = fixture.Repository;
        await repository.InitializeAsync();

        // Hold the lock file exclusively, as FileLockRepositoryCoordinator itself does, so the
        // coordinator's retry loop exhausts options.EffectiveLockTimeout and raises SNR-GIT-004.
        using var externalLock = new FileStream(fixture.LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitPlanAsync(new PlanCommitRequest(SamplePlan(), "author", "should fail", "authoring")).AsTask());

        Assert.Equal("SNR-GIT-004", exception.Code);
        Assert.True(exception.Retryable);
    }

    [Fact]
    public async Task ApproveAsync_creates_monotonic_tags_and_is_idempotent_for_the_same_commit()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan();
        var commit = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "initial plan", "authoring"));

        var firstApproval = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit.CommitId);
        var secondApproval = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit.CommitId);

        Assert.Equal(firstApproval.ApprovalTag, secondApproval.ApprovalTag);
        Assert.EndsWith("/1", firstApproval.ApprovalTag!, StringComparison.Ordinal);

        var updatedPlan = plan with { Provenance = plan.Provenance with { Score = 0.5d } };
        var secondCommit = await repository.CommitPlanAsync(new PlanCommitRequest(updatedPlan, "heal", "healed plan", "heal:test"));
        var thirdApproval = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, secondCommit.CommitId);

        Assert.EndsWith("/2", thirdApproval.ApprovalTag!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ApproveAsync_throws_SNR_GIT_006_when_the_computed_next_tag_is_created_concurrently()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan();
        var commit = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "initial plan", "authoring"));
        var updatedPlan = plan with { Provenance = plan.Provenance with { Score = 0.5d } };
        var conflictingCommit = await repository.CommitPlanAsync(new PlanCommitRequest(updatedPlan, "heal", "healed plan", "heal:test"));

        // SNR-GIT-006 is a TOCTOU race: an external process creates the exact tag this call is about to
        // add, in the window between this call computing the next monotonic tag name and it checking
        // whether that name is already taken. GitScriptRepository.ApprovalConflictSimulation is an
        // AsyncLocal test-only seam that runs right at that boundary, letting the test insert the
        // conflicting tag deterministically instead of relying on a real race between threads/processes.
        GitScriptRepository.ApprovalConflictSimulation = (repo, tagName) => repo.Tags.Add(tagName, repo.Lookup<Commit>(conflictingCommit.CommitId)!);
        try
        {
            var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
                () => repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit.CommitId).AsTask());

            Assert.Equal("SNR-GIT-006", exception.Code);
            Assert.False(exception.Retryable);
            Assert.Equal("Approval tag 'approved/lenovo/tablets/Product@1/1' already points at a different commit.", exception.Message);
        }
        finally
        {
            GitScriptRepository.ApprovalConflictSimulation = null;
        }

        var tags = await repository.GetApprovalTagsAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);
        Assert.Single(tags);
        Assert.Equal(conflictingCommit.CommitId, tags[0].CommitId);
    }

    [Fact]
    public async Task CommitPlanAsync_with_Approve_tags_inline()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan();

        var commit = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "initial plan", "authoring", Approve: true));

        Assert.NotNull(commit.ApprovalTag);
        var tags = await repository.GetApprovalTagsAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);
        Assert.Single(tags);
        Assert.Equal(commit.CommitId, tags[0].CommitId);
    }

    [Fact]
    public async Task GetApprovalTagsAsync_only_returns_tags_for_the_requested_source_schema_version()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan();
        var otherPlan = plan with { SourceId = "other/source" };

        var commit = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "initial plan", "authoring", Approve: true));
        await repository.CommitPlanAsync(new PlanCommitRequest(otherPlan, "author", "other plan", "authoring", Approve: true));

        var tags = await repository.GetApprovalTagsAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);

        Assert.Single(tags);
        Assert.Equal(commit.CommitId, tags[0].CommitId);
    }

    [Fact]
    public async Task CommitPlanAsync_throws_SNR_GIT_015_when_sourceId_contains_path_traversal()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan() with { SourceId = "lenovo/../../../etc/passwd" };

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "should fail", "authoring")).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    [Fact]
    public async Task CommitPlanAsync_throws_SNR_GIT_015_when_sourceId_contains_uppercase()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan() with { SourceId = "Lenovo/Tablets" };

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "should fail", "authoring")).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    [Fact]
    public async Task CommitPlanAsync_throws_SNR_GIT_015_when_schemaName_contains_invalid_characters()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan() with { SchemaName = "Product/../Evil" };

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "should fail", "authoring")).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    [Fact]
    public async Task CommitPlanAsync_accepts_valid_sourceId_with_slash_segments()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan() with { SourceId = "lenovo/tablets/products" };

        var commitInfo = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "multi-segment source", "authoring"));

        Assert.NotNull(commitInfo);
        Assert.NotEmpty(commitInfo.CommitId);
    }

    [Fact]
    public async Task CommitPlanAsync_accepts_valid_PascalCase_schemaName()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var plan = SamplePlan() with { SchemaName = "TabletListing" };

        var commitInfo = await repository.CommitPlanAsync(new PlanCommitRequest(plan, "author", "pascalcase schema", "authoring"));

        Assert.NotNull(commitInfo);
        Assert.NotEmpty(commitInfo.CommitId);
    }

    [Fact]
    public async Task GetApprovalTagsAsync_throws_SNR_GIT_015_when_sourceId_has_ref_injection()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.GetApprovalTagsAsync("lenovo/tablets; refs/heads/main/", "Product", 1).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    private GitScriptRepository CreateRepository() => CreateRepository(out _);

    private GitScriptRepository CreateRepository(out ScriptRepositoryOptions options)
    {
        var fixture = new ScriptRepositoryFixture();
        _fixtures.Add(fixture);
        options = fixture.Options;
        return fixture.Repository;
    }

    private static ExtractionPlan SamplePlan() => ScriptRepositoryFixture.SamplePlan();

    public void Dispose()
    {
        foreach (var fixture in _fixtures)
        {
            fixture.Dispose();
        }
    }
}
