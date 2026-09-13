using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-011 and AC-GIT-012. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryDiffTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task DiffAsync_reports_the_changed_plan_file_with_line_counts_and_patch_text()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 2);

        var diff = await repository.DiffAsync(commits[0], commits[1]);

        Assert.Equal(commits[0], diff.FromCommitId);
        Assert.Equal(commits[1], diff.ToCommitId);
        var file = Assert.Single(diff.Files);
        Assert.Equal("plans/lenovo/tablets/Product@1.plan.json", file.Path);
        Assert.Equal("modified", file.Status);
        Assert.True(file.LinesAdded > 0);
        Assert.True(file.LinesDeleted > 0);
        Assert.Contains("Product@1.plan.json", file.Patch, StringComparison.Ordinal);
        Assert.False(file.Truncated);
    }

    [Fact]
    public async Task DiffAsync_reports_a_newly_added_plan_as_added()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var status = await repository.GetStatusAsync();
        var baseCommit = status.HeadCommitId!;

        var plan = ScriptRepositoryFixture.SamplePlan();
        var added = await _fixture.CommitPlanAsync(plan);

        var diff = await repository.DiffAsync(baseCommit, added);

        var file = Assert.Single(diff.Files);
        Assert.Equal("added", file.Status);
        Assert.Equal(0, file.LinesDeleted);
    }

    [Fact]
    public async Task DiffAsync_ignores_changes_outside_the_plans_tree()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var planCommit = await _fixture.CommitPlanAsync(plan);
        await repository.CommitDiagnosisNoteAsync(plan.SourceId, "# note\n\nnothing to see here.\n", planCommit);

        var after = (await repository.GetStatusAsync()).HeadCommitId!;
        var diff = await repository.DiffAsync(planCommit, after);

        Assert.Empty(diff.Files);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DiffAsync_throws_SNR_GIT_002_for_a_commit_id_that_does_not_resolve(bool badFromSide)
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var known = await _fixture.CommitPlanAsync(plan);
        const string Unknown = "0123456789abcdef0123456789abcdef01234567";

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.DiffAsync(badFromSide ? Unknown : known, badFromSide ? known : Unknown).AsTask());

        Assert.Equal("SNR-GIT-002", exception.Code);
    }

    public void Dispose() => _fixture.Dispose();
}
