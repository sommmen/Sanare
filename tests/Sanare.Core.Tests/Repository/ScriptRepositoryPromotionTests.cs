using LibGit2Sharp;
using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-014 and AC-GIT-017. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryPromotionTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task PromoteAsync_fast_forwards_the_default_branch_and_creates_the_next_approval_tag()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        var healed = await _fixture.CommitPlanOnAsync(branch, plan with { SchemaHash = "healed" }, "heal attempt");

        var promoted = await repository.PromoteAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, branch);

        Assert.Equal(healed, promoted.CommitId);
        Assert.Equal("approved/lenovo/tablets/Product@1/2", promoted.ApprovalTag);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Equal(healed, repo.Branches[_fixture.Options.DefaultBranch]!.Tip!.Sha);

        // Fast-forward only: the promoted tip must have exactly one parent, never a merge commit.
        Assert.Single(repo.Lookup<Commit>(healed)!.Parents);
    }

    [Fact]
    public async Task PromoteAsync_throws_SNR_GIT_005_and_moves_no_ref_when_the_branch_has_diverged()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        await _fixture.CommitPlanOnAsync(branch, plan with { SchemaHash = "healed" }, "heal attempt");

        // The default branch moves on independently, so the heal branch is no longer a descendant.
        var diverged = await _fixture.CommitPlanAsync(plan with { SchemaHash = "mainline" }, "mainline edit");

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.PromoteAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, branch).AsTask());

        Assert.Equal("SNR-GIT-005", exception.Code);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Equal(diverged, repo.Branches[_fixture.Options.DefaultBranch]!.Tip!.Sha);
        Assert.Null(repo.Tags["approved/lenovo/tablets/Product@1/2"]);
    }

    [Fact]
    public async Task PromoteAsync_throws_SNR_GIT_002_for_an_unknown_branch()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();
        await _fixture.CommitPlanAsync(ScriptRepositoryFixture.SamplePlan());

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.PromoteAsync("lenovo/tablets", "Product", 1, "heal/nope").AsTask());

        Assert.Equal("SNR-GIT-002", exception.Code);
    }

    [Fact]
    public async Task PruneHealBranchesAsync_deletes_promoted_branches_but_keeps_tagged_and_recent_ones()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        var promotedBranch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "promoted");
        await _fixture.CommitPlanOnAsync(promotedBranch, plan with { SchemaHash = "healed" }, "heal attempt");
        await repository.PromoteAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, promotedBranch);

        var recentBranch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "recent");
        await _fixture.CommitPlanOnAsync(recentBranch, plan with { SchemaHash = "in-flight" }, "still working");

        // A one-day window keeps the just-created branch; the promoted one goes regardless of age.
        var deleted = await repository.PruneHealBranchesAsync(TimeSpan.FromDays(1));

        Assert.Equal([promotedBranch], deleted);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Null(repo.Branches[promotedBranch]);
        Assert.NotNull(repo.Branches[recentBranch]);
    }

    [Fact]
    public async Task PruneHealBranchesAsync_deletes_stale_untagged_branches_when_the_window_is_zero()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        var stale = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "stale");
        await _fixture.CommitPlanOnAsync(stale, plan with { SchemaHash = "abandoned" }, "abandoned attempt");

        var tagged = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "tagged");
        var taggedTip = await _fixture.CommitPlanOnAsync(tagged, plan with { SchemaHash = "tagged" }, "tagged attempt");
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, taggedTip);

        var deleted = await repository.PruneHealBranchesAsync(TimeSpan.Zero);

        Assert.Equal([stale], deleted);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Null(repo.Branches[stale]);
        Assert.NotNull(repo.Branches[tagged]);
    }

    [Fact]
    public async Task PruneHealBranchesAsync_never_touches_the_plans_tree()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var original = await _fixture.CommitPlanAsync(plan);
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, original);

        var stale = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "stale");
        await _fixture.CommitPlanOnAsync(stale, plan with { SchemaHash = "abandoned" }, "abandoned attempt");

        await repository.PruneHealBranchesAsync(TimeSpan.Zero);

        var document = await repository.GetPlanAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);
        Assert.NotNull(document);
    }

    [Fact]
    public async Task PruneHealBranchesAsync_rejects_a_negative_window_with_SNR_GIT_015()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.PruneHealBranchesAsync(TimeSpan.FromSeconds(-1)).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    public void Dispose() => _fixture.Dispose();
}
