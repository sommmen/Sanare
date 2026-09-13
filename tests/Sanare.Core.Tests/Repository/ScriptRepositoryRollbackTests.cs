using LibGit2Sharp;
using Sanare.Core.Plans;
using Sanare.Core.Repository;
using Sanare.Core.Resolution;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-015, AC-GIT-016, and AC-015. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryRollbackTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task RollbackAsync_creates_a_new_higher_tag_at_the_older_commit_and_keeps_the_superseded_one()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 2);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[0]);
        var superseded = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[1]);

        var rolledBack = await repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[0]);

        Assert.Equal(commits[0], rolledBack.CommitId);
        Assert.Equal("approved/lenovo/tablets/Product@1/3", rolledBack.ApprovalTag);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.NotNull(repo.Tags[superseded.ApprovalTag]);
        Assert.NotNull(repo.Tags["approved/lenovo/tablets/Product@1/1"]);

        // Rollback re-points approval; it commits nothing, so the default branch tip is unmoved.
        Assert.Equal(commits[1], repo.Branches[_fixture.Options.DefaultBranch]!.Tip!.Sha);
    }

    [Fact]
    public async Task PlanResolver_serves_the_rolled_back_plan_after_invalidation()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var first = await _fixture.CommitPlanAsync(plan);
        var second = await _fixture.CommitPlanAsync(plan with { SchemaHash = "def456" }, "second revision");
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, first);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, second);

        var resolver = new PlanResolver(repository, new PlanSerializer());
        var request = new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "def456");
        var warm = await resolver.ResolveAsync(request);
        Assert.True(warm.IsResolved);
        Assert.Equal(second, warm.Plan!.CommitId);

        await repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, first);
        resolver.Invalidate(plan.SourceId);

        var resolved = await resolver.ResolveAsync(new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "abc123"));

        Assert.True(resolved.IsResolved);
        Assert.Equal(first, resolved.Plan!.CommitId);
        Assert.Equal("abc123", resolved.Plan.Plan.SchemaHash);
    }

    [Fact]
    public async Task RollbackAsync_to_the_already_approved_commit_is_an_idempotent_no_op()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 2);
        var approved = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[1]);

        var rolledBack = await repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[1]);

        Assert.Equal(approved.ApprovalTag, rolledBack.ApprovalTag);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Single(repo.Tags, t => t.FriendlyName.StartsWith("approved/lenovo/tablets/Product@1/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RollbackAsync_throws_SNR_GIT_002_for_an_unknown_target_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commit = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit);

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "0123456789abcdef0123456789abcdef01234567").AsTask());

        Assert.Equal("SNR-GIT-002", exception.Code);
    }

    [Fact]
    public async Task RollbackAsync_throws_SNR_GIT_005_when_the_target_is_not_an_ancestor_of_the_approved_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        // A sibling commit on a heal branch is a descendant of the approved commit, never an ancestor.
        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        var sibling = await _fixture.CommitPlanOnAsync(branch, plan with { SchemaHash = "healed" }, "heal attempt");

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, sibling).AsTask());

        Assert.Equal("SNR-GIT-005", exception.Code);
    }

    [Fact]
    public async Task RollbackAsync_throws_SNR_GIT_002_when_the_plan_does_not_exist_at_the_target_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var status = await repository.GetStatusAsync();
        var scaffold = status.HeadCommitId!;

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commit = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit);

        // The scaffold commit is a valid ancestor, but the plan file does not exist there yet.
        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, scaffold).AsTask());

        Assert.Equal("SNR-GIT-002", exception.Code);
    }

    [Fact]
    public async Task RollbackAsync_throws_SNR_GIT_005_when_nothing_has_ever_been_approved()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commit = await _fixture.CommitPlanAsync(plan);

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.RollbackAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commit).AsTask());

        Assert.Equal("SNR-GIT-005", exception.Code);
    }

    public void Dispose() => _fixture.Dispose();
}
