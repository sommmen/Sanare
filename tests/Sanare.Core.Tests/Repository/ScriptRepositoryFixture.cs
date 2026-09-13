using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;

namespace Sanare.Core.Tests.Repository;

/// <summary>
/// A disposable temporary <see cref="GitScriptRepository"/> plus the history-shaping helpers the
/// advanced-operation suites need. See docs/features/script-repository.md ("Implementation Plan" → T2).
/// </summary>
public sealed class ScriptRepositoryFixture : IDisposable
{
    private static readonly Uri ProductUrl = new("https://example.test/products/1");

    public ScriptRepositoryFixture(TimeSpan? lockTimeout = null, string? stateRoot = null)
    {
        StateRoot = stateRoot ?? Path.Combine(Path.GetTempPath(), "sanare-tests", Guid.NewGuid().ToString("N"));
        Options = new ScriptRepositoryOptions(StateRoot, LockTimeout: lockTimeout);
        LockPath = Path.Combine(Options.RepositoryPath, ".sanare-lock");
        Repository = new GitScriptRepository(Options, new PlanSerializer(), new PlanValidator(), new FileLockRepositoryCoordinator(LockPath));
    }

    public string StateRoot { get; }

    public ScriptRepositoryOptions Options { get; }

    /// <summary>The coordinator's lock file; tests hold it exclusively to force <c>SNR-GIT-004</c>.</summary>
    public string LockPath { get; }

    public GitScriptRepository Repository { get; }

    public async Task<ScriptRepositoryFixture> InitializedAsync()
    {
        await Repository.InitializeAsync();
        return this;
    }

    /// <summary>Commits <paramref name="plan"/> to the default branch and returns the new commit's id.</summary>
    public Task<string> CommitPlanAsync(ExtractionPlan plan, string summary = "test plan", string verb = "author", string reason = "authoring") =>
        CommitPlanOnAsync(null, plan, summary, verb, reason);

    /// <summary>Commits <paramref name="plan"/> on <paramref name="branch"/> and returns the new commit's id.</summary>
    public async Task<string> CommitPlanOnAsync(
        string? branch, ExtractionPlan plan, string summary = "test plan", string verb = "author", string reason = "authoring")
    {
        var info = await Repository.CommitPlanAsync(new PlanCommitRequest(plan, verb, summary, reason, branch));
        return info.CommitId;
    }

    /// <summary>
    /// Commits <paramref name="count"/> successive revisions of <paramref name="plan"/>, each distinguished by
    /// its provenance score so the canonical JSON differs, and returns the commit ids oldest-first.
    /// </summary>
    public async Task<IReadOnlyList<string>> CommitRevisionsAsync(ExtractionPlan plan, int count, string? branch = null)
    {
        var commits = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var revision = plan with { Provenance = plan.Provenance with { Score = Math.Round(0.10d * (i + 1), 4) } };
            commits.Add(await CommitPlanOnAsync(branch, revision, $"revision {i + 1}"));
        }

        return commits;
    }

    public static ExtractionPlan SamplePlan() => new()
    {
        PlanVersion = ExtractionPlan.CurrentPlanVersion,
        SourceId = "lenovo/tablets",
        SchemaName = "Product",
        SchemaVersion = 1,
        SchemaHash = "abc123",
        Culture = "en-US",
        Tier = AcquisitionTier.Html,
        Acquisition = new AcquisitionSpec(AcquisitionMethod.Get, ProductUrl.AbsoluteUri, new Dictionary<string, string>(), null, Array.Empty<InteractionStep>()),
        Fields =
        [
            new FieldPlan("/Name", true, "string", [new LocatorStep(PlanOperation.SelectFirst, [".name"])], [new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
            new FieldPlan("/Price", true, "decimal", [new LocatorStep(PlanOperation.SelectFirst, [".price"])], [new TransformStep(PlanOperation.StripCurrency, Array.Empty<string>()), new TransformStep(PlanOperation.Trim, Array.Empty<string>())]),
        ],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 0.9d, DateTimeOffset.UnixEpoch),
    };

    public void Dispose()
    {
        if (!Directory.Exists(StateRoot))
        {
            return;
        }

        NormalizeAttributes(StateRoot);
        Directory.Delete(StateRoot, recursive: true);
    }

    private static void NormalizeAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
