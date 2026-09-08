using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;
using Sanare.Core.Resolution;
using Xunit;

namespace Sanare.Core.Tests.Resolution;

public sealed class PlanResolverTests
{
    private static readonly Uri ProductUrl = new("https://example.test/products/1");

    [Fact]
    public async Task ResolveAsync_returns_NoPlanAvailable_when_no_approval_tags_exist()
    {
        var repository = new FakeScriptRepository();
        var resolver = new PlanResolver(repository, new PlanSerializer());

        var resolution = await resolver.ResolveAsync(new PlanResolutionRequest("lenovo/tablets", "Product", 1, "abc123"));

        Assert.False(resolution.IsResolved);
        Assert.Equal(PlanResolutionFailure.NoPlanAvailable, resolution.Failure);
        Assert.Equal("SNR-PLAN-004", resolution.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_resolves_the_highest_numbered_approval_tag()
    {
        var plan = SamplePlan();
        var repository = new FakeScriptRepository();
        repository.AddApprovedPlan(plan, "commit-9", "approved/lenovo/tablets/Product@1/9");
        repository.AddApprovedPlan(plan, "commit-10", "approved/lenovo/tablets/Product@1/10");

        var resolver = new PlanResolver(repository, new PlanSerializer());
        var resolution = await resolver.ResolveAsync(new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, plan.SchemaHash));

        Assert.True(resolution.IsResolved);
        Assert.Equal("commit-10", resolution.Plan!.CommitId);
        Assert.Equal("approved/lenovo/tablets/Product@1/10", resolution.Plan!.ApprovalTag);
    }

    [Fact]
    public async Task ResolveAsync_caches_the_resolution_and_does_not_hit_the_repository_again()
    {
        var plan = SamplePlan();
        var repository = new FakeScriptRepository();
        repository.AddApprovedPlan(plan, "commit-1", "approved/lenovo/tablets/Product@1/1");
        var resolver = new PlanResolver(repository, new PlanSerializer());
        var request = new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, plan.SchemaHash);

        await resolver.ResolveAsync(request);
        var tagCallsAfterFirst = repository.GetApprovalTagsCallCount;
        await resolver.ResolveAsync(request);

        Assert.Equal(tagCallsAfterFirst, repository.GetApprovalTagsCallCount);
    }

    [Fact]
    public async Task ResolveAsync_returns_SchemaDrift_when_schema_hash_differs()
    {
        var plan = SamplePlan();
        var repository = new FakeScriptRepository();
        repository.AddApprovedPlan(plan, "commit-1", "approved/lenovo/tablets/Product@1/1");
        var resolver = new PlanResolver(repository, new PlanSerializer());

        var resolution = await resolver.ResolveAsync(new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, "different-hash"));

        Assert.False(resolution.IsResolved);
        Assert.Equal(PlanResolutionFailure.SchemaDrift, resolution.Failure);
        Assert.Equal("SNR-PLAN-003", resolution.ErrorCode);
    }

    [Fact]
    public async Task ResolveAsync_returns_PlanInvalid_when_the_stored_document_is_malformed()
    {
        var repository = new FakeScriptRepository();
        repository.AddRawDocument("lenovo/tablets", "Product", 1, "{ not json", "commit-1", "approved/lenovo/tablets/Product@1/1");
        var resolver = new PlanResolver(repository, new PlanSerializer());

        var resolution = await resolver.ResolveAsync(new PlanResolutionRequest("lenovo/tablets", "Product", 1, "abc123"));

        Assert.False(resolution.IsResolved);
        Assert.Equal(PlanResolutionFailure.PlanInvalid, resolution.Failure);
        Assert.Equal("SNR-PLAN-001", resolution.ErrorCode);
    }

    [Fact]
    public async Task Invalidate_clears_the_cached_entry_so_the_next_resolve_hits_the_repository()
    {
        var plan = SamplePlan();
        var repository = new FakeScriptRepository();
        repository.AddApprovedPlan(plan, "commit-1", "approved/lenovo/tablets/Product@1/1");
        var resolver = new PlanResolver(repository, new PlanSerializer());
        var request = new PlanResolutionRequest(plan.SourceId, plan.SchemaName, plan.SchemaVersion, plan.SchemaHash);
        await resolver.ResolveAsync(request);
        var callsBeforeInvalidate = repository.GetApprovalTagsCallCount;

        resolver.Invalidate(plan.SourceId);
        await resolver.ResolveAsync(request);

        Assert.True(repository.GetApprovalTagsCallCount > callsBeforeInvalidate);
    }

    private static ExtractionPlan SamplePlan() => new()
    {
        PlanVersion = 1,
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
        ],
        Provenance = new PlanProvenance("test", "none", 1, Array.Empty<string>(), 0.9d, DateTimeOffset.UnixEpoch),
    };

    private sealed class FakeScriptRepository : IScriptRepository
    {
        private readonly List<(string SourceId, string SchemaName, int SchemaVersion, string Json, string CommitId, string TagName)> _entries = [];

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
    }
}
