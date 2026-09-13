using LibGit2Sharp;
using Sanare.Core.Repository;
using Xunit;

namespace Sanare.Core.Tests.Repository;

/// <summary>Covers AC-GIT-013 and AC-025. See docs/features/script-repository.md.</summary>
public sealed class ScriptRepositoryHealBranchTests : IDisposable
{
    private readonly ScriptRepositoryFixture _fixture = new();

    [Fact]
    public async Task CreateHealBranchAsync_names_the_branch_from_the_source_id_date_and_reason()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        await _fixture.CommitPlanAsync(plan);

        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "Selector Drift");

        Assert.Equal($"heal/lenovo/tablets/{DateTimeOffset.UtcNow:yyyyMMdd}-selector-drift", branch);
    }

    [Fact]
    public async Task CreateHealBranchAsync_branches_from_the_approved_commit_not_from_head()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commits = await _fixture.CommitRevisionsAsync(plan, 3);

        // Approve the middle revision, then let HEAD advance past it.
        await repository.ApproveAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, commits[1]);

        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Equal(commits[1], repo.Branches[branch]!.Tip!.Sha);
        Assert.Equal(commits[2], repo.Branches[_fixture.Options.DefaultBranch]!.Tip!.Sha);
    }

    [Fact]
    public async Task CreateHealBranchAsync_falls_back_to_the_default_branch_tip_when_nothing_is_approved()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        var commit = await _fixture.CommitPlanAsync(plan);

        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        Assert.Equal(commit, repo.Branches[branch]!.Tip!.Sha);
    }

    [Fact]
    public async Task CreateHealBranchAsync_suffixes_the_name_on_a_same_day_collision()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        await _fixture.CommitPlanAsync(plan);

        var first = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        var second = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        var third = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");

        Assert.Equal(first + "-2", second);
        Assert.Equal(first + "-3", third);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..")]
    [InlineData("   ")]
    [InlineData("///")]
    public async Task CreateHealBranchAsync_rejects_a_reason_that_could_inject_a_ref_path(string reason)
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();
        await _fixture.CommitPlanAsync(ScriptRepositoryFixture.SamplePlan());

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CreateHealBranchAsync("lenovo/tablets", "Product", 1, reason).AsTask());

        Assert.Equal("SNR-GIT-015", exception.Code);
    }

    [Fact]
    public async Task CommitDiagnosisNoteAsync_writes_the_note_on_the_branch_holding_the_related_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();

        var plan = ScriptRepositoryFixture.SamplePlan();
        await _fixture.CommitPlanAsync(plan);
        var branch = await repository.CreateHealBranchAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "drift");
        var healCommit = await _fixture.CommitPlanOnAsync(branch, plan with { SchemaHash = "healed" }, "heal attempt");

        await repository.CommitDiagnosisNoteAsync(plan.SourceId, "# Diagnosis\n\nThe `.price` selector moved.\n", healCommit);

        using var repo = new LibGit2Sharp.Repository(_fixture.Options.RepositoryPath);
        var tip = repo.Branches[branch]!.Tip!;
        var note = tip.Tree["notes"];
        Assert.NotNull(note);

        var noteBlob = tip.Tree
            .Single(e => e.Name == "notes").Target
            .Peel<Tree>().Single(e => e.Name == "lenovo").Target
            .Peel<Tree>().Single(e => e.Name == "tablets").Target
            .Peel<Tree>().Single();
        Assert.EndsWith($"-{healCommit[..7]}.md", noteBlob.Name, StringComparison.Ordinal);

        // The default branch is untouched, and the note never shows up in plan history.
        Assert.Null(repo.Branches[_fixture.Options.DefaultBranch]!.Tip!.Tree["notes"]);
        var history = await repository.GetHistoryAsync(plan.SourceId, plan.SchemaName, plan.SchemaVersion);
        Assert.DoesNotContain(history.Entries, e => e.Summary.Contains("diagnosis", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CommitDiagnosisNoteAsync_throws_SNR_GIT_002_for_an_unknown_related_commit()
    {
        var repository = _fixture.Repository;
        await repository.InitializeAsync();
        await _fixture.CommitPlanAsync(ScriptRepositoryFixture.SamplePlan());

        var exception = await Assert.ThrowsAsync<ScriptRepositoryException>(
            () => repository.CommitDiagnosisNoteAsync("lenovo/tablets", "# note", "0123456789abcdef0123456789abcdef01234567").AsTask());

        Assert.Equal("SNR-GIT-002", exception.Code);
    }

    public void Dispose() => _fixture.Dispose();
}
