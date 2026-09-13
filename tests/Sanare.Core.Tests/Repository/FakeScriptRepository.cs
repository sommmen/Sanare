using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;

namespace Sanare.Core.Tests.Repository;

/// <summary>
/// An in-memory <see cref="IScriptRepository"/> serving pre-seeded plan documents and approval tags,
/// with every other member unsupported. It lives here rather than nested in a consumer's test class so
/// that adding an interface member breaks exactly one file. See docs/features/script-repository.md
/// ("Implementation Plan" → T2).
/// </summary>
public sealed class FakeScriptRepository : IScriptRepository
{
    private readonly List<(string SourceId, string SchemaName, int SchemaVersion, string Json, string CommitId, string TagName)> _entries = [];

    /// <summary>How many times the plan resolver has gone past its warm index to this repository.</summary>
    public int GetApprovalTagsCallCount { get; private set; }

    public void AddApprovedPlan(ExtractionPlan plan, string commitId, string tagName) =>
        AddRawDocument(plan.SourceId, plan.SchemaName, plan.SchemaVersion, new PlanSerializer().WriteCanonical(plan), commitId, tagName);

    public void AddRawDocument(string sourceId, string schemaName, int schemaVersion, string json, string commitId, string tagName) =>
        _entries.Add((sourceId, schemaName, schemaVersion, json, commitId, tagName));

    public ValueTask InitializeAsync(CancellationToken ct = default) => ValueTask.CompletedTask;

    public ValueTask<PlanDocument?> GetPlanAsync(string sourceId, string schemaName, int schemaVersion, GitRef? reference = null, CancellationToken ct = default)
    {
        var match = _entries.FirstOrDefault(e => e.CommitId == reference!.Value);
        if (match.Json is null)
        {
            return ValueTask.FromResult<PlanDocument?>(null);
        }

        return ValueTask.FromResult<PlanDocument?>(new PlanDocument(match.Json, match.CommitId, reference!.Value, DateTimeOffset.UnixEpoch, "test"));
    }

    public ValueTask<PlanCommitInfo> CommitPlanAsync(PlanCommitRequest request, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<PlanCommitInfo> ApproveAsync(string sourceId, string schemaName, int schemaVersion, string commitId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<RepositoryStatus> GetStatusAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<ApprovalTagEntry>> GetApprovalTagsAsync(string sourceId, string schemaName, int schemaVersion, CancellationToken ct = default)
    {
        GetApprovalTagsCallCount++;
        var tags = _entries
            .Where(e => e.SourceId == sourceId && e.SchemaName == schemaName && e.SchemaVersion == schemaVersion)
            .Select(e => new ApprovalTagEntry(e.TagName, e.CommitId))
            .ToArray();
        return ValueTask.FromResult<IReadOnlyList<ApprovalTagEntry>>(tags);
    }

    public ValueTask<PlanHistory> GetHistoryAsync(string sourceId, string schemaName, int schemaVersion, int? limit = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<PlanDiff> DiffAsync(string fromCommitId, string toCommitId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<string> CreateHealBranchAsync(string sourceId, string schemaName, int schemaVersion, string shortReason, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<PlanCommitInfo> PromoteAsync(string sourceId, string schemaName, int schemaVersion, string branch, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<PlanCommitInfo> RollbackAsync(string sourceId, string schemaName, int schemaVersion, string targetCommitId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask CommitDiagnosisNoteAsync(string sourceId, string markdown, string relatedCommitId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public ValueTask<IReadOnlyList<string>> PruneHealBranchesAsync(TimeSpan olderThan, CancellationToken ct = default) =>
        throw new NotSupportedException();
}
