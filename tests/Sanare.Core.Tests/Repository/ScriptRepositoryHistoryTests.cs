using Sanare.Abstractions.Plans;
using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-009, AC-GIT-010, and AC-024. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryHistoryTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task GetHistoryAsync_returns_only_the_requested_plans_commits_newest_first()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 4);

        // A second plan under the same source must not leak into the first plan's history.
        await _fixture.CommitPlanAsync(plan with { SchemaName = "Review", SchemaHash = "def456" }, "other plan");

        var history = await repository.GetHistoryAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);

        Assert.Equal("plans/lenovo/tablets/Product@1.plan.json", history.PlanPath);
        Assert.Equal(4, history.Entries.Count);
        Assert.Equal(commits.Reverse().ToArray(), history.Entries.Select(e => e.Commit.CommitId).ToArray());
    }

    [Fact]
    public async Task GetHistoryAsync_honors_limit_and_reports_the_approval_tag_pointing_at_each_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 3);
        var approved = await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[^1]);

        var history = await repository.GetHistoryAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, limit: 2);

        Assert.Equal(2, history.Entries.Count);
        Assert.Equal(approved.ApprovalTag, history.Entries[0].Commit.ApprovalTag);
        Assert.Null(history.Entries[1].Commit.ApprovalTag);
    }

    [Fact]
    public async Task GetHistoryAsync_reads_provenance_back_out_of_the_commit_message_trailers()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan() with
        {
            Provenance = new PlanProvenance("agent", "gpt-test", 3, ["fixture-a", "fixture-b"], 0.8125d, DateTimeOffset.UnixEpoch),
        };
        await _fixture.CommitPlanAsync(plan, "with provenance", reason: "schema drift detected");

        var history = await repository.GetHistoryAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);
        var entry = Assert.Single(history.Entries);

        Assert.True(entry.HasProvenance);
        Assert.Equal("Product", entry.Commit.SchemaName);
        Assert.Equal(1, entry.Commit.SchemaVersion);
        Assert.Equal("abc123", entry.Commit.SchemaHash);
        Assert.Equal("gpt-test", entry.Commit.Model);
        Assert.Equal(3, entry.Commit.Attempts);
        Assert.Equal(["fixture-a", "fixture-b"], entry.Commit.FixtureIds);
        Assert.Equal(0.8125d, entry.Commit.Score, 4);
        Assert.Equal("schema drift detected", entry.Commit.Reason);
    }

    [Fact]
    public async Task GetHistoryAsync_returns_an_empty_history_for_a_plan_that_was_never_committed()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var history = await repository.GetHistoryAsync("lenovo/tablets", "Product", 1);

        Assert.Empty(history.Entries);
    }

    [Fact]
    public async Task GetHistoryAsync_rejects_a_non_positive_limit_with_SNR_GIT_015()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.GetHistoryAsync("lenovo/tablets", "Product", 1, limit: 0).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    public void Dispose() => _fixture.Dispose();
}
