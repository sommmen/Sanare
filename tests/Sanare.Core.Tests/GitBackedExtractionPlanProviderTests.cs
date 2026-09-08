using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Plans;
using Sanare.Core.Repository;
using Sanare.Core.Resolution;
using Sanare.Core.Schema;

namespace Sanare.Core.Tests;

public sealed class GitBackedExtractionPlanProviderTests : IDisposable
{
    private static readonly Uri ProductUrl = new("https://example.test/products/1");
    private readonly string _stateRoot = Path.Combine(Path.GetTempPath(), "sanare-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TryGet_returns_resolved_plan_from_the_latest_approval_tag()
    {
        var options = new ScriptRepositoryOptions(_stateRoot);
        var repository = CreateRepository(options);
        await repository.InitializeAsync();
        var firstPlan = SamplePlan();
        await repository.CommitPlanAsync(new PlanCommitRequest(firstPlan, "author", "initial plan", "authoring", Approve: true));
        var approvedPlan = firstPlan with { Provenance = firstPlan.Provenance with { Score = 0.5d } };
        await repository.CommitPlanAsync(new PlanCommitRequest(approvedPlan, "heal", "healed plan", "heal:test", Approve: true));
        var provider = new GitBackedExtractionPlanProvider(
            new PlanResolver(repository, new PlanSerializer()),
            new FixedSchemaDeriver(approvedPlan.SchemaName, approvedPlan.SchemaVersion, approvedPlan.SchemaHash));

        var found = provider.TryGet(approvedPlan.SourceId, typeof(ProductSchema), out var plan);

        Assert.True(found);
        Assert.Equivalent(approvedPlan, plan);
        Assert.Equal(0.5d, plan.Provenance.Score);
    }

    [Theory]
    [InlineData(PlanResolutionFailure.NoPlanAvailable, "SNR-PLAN-004")]
    [InlineData(PlanResolutionFailure.SchemaDrift, "SNR-PLAN-003")]
    [InlineData(PlanResolutionFailure.PlanInvalid, "SNR-PLAN-001")]
    public void TryGet_returns_false_when_resolution_fails(PlanResolutionFailure failure, string errorCode)
    {
        var provider = new GitBackedExtractionPlanProvider(
            new FixedPlanResolver(PlanResolution.Failed(failure, errorCode, "resolution failed")),
            new FixedSchemaDeriver("Product", 1, "schema-hash"));

        var found = provider.TryGet("lenovo/tablets", typeof(ProductSchema), out var plan);

        Assert.False(found);
        Assert.Null(plan);
    }

    [Fact]
    public void TryGet_passes_derived_schema_metadata_to_the_resolver()
    {
        var resolver = new RecordingPlanResolver();
        var provider = new GitBackedExtractionPlanProvider(
            resolver,
            new FixedSchemaDeriver("Product", 2, "derived-hash"));

        _ = provider.TryGet("lenovo/tablets", typeof(ProductSchema), out _);

        Assert.Equal(new PlanResolutionRequest("lenovo/tablets", "Product", 2, "derived-hash"), resolver.Request);
    }

    [Fact]
    public void TryGet_throws_for_null_arguments()
    {
        var provider = new GitBackedExtractionPlanProvider(
            new FixedPlanResolver(PlanResolution.Failed(PlanResolutionFailure.NoPlanAvailable, "SNR-PLAN-004", "missing")),
            new FixedSchemaDeriver("Product", 1, "schema-hash"));

        Assert.Throws<ArgumentNullException>(() => provider.TryGet(null!, typeof(ProductSchema), out _));
        Assert.Throws<ArgumentNullException>(() => provider.TryGet("lenovo/tablets", null!, out _));
    }

    [Fact]
    public void TryGet_propagates_resolver_exceptions()
    {
        var provider = new GitBackedExtractionPlanProvider(
            new ThrowingPlanResolver(),
            new FixedSchemaDeriver("Product", 1, "schema-hash"));

        var exception = Assert.Throws<InvalidOperationException>(() => provider.TryGet("lenovo/tablets", typeof(ProductSchema), out _));

        Assert.Equal("resolver failure", exception.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateRoot))
        {
            NormalizeAttributes(_stateRoot);
            Directory.Delete(_stateRoot, recursive: true);
        }
    }

    private static GitScriptRepository CreateRepository(ScriptRepositoryOptions options) =>
        new(options, new PlanSerializer(), new FileLockRepositoryCoordinator(Path.Combine(options.RepositoryPath, ".sanare-lock")));

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

    private static void NormalizeAttributes(string root)
    {
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }

    private sealed class ProductSchema;

    private sealed class FixedSchemaDeriver(string name, int version, string hash) : ISchemaDeriver
    {
        private readonly SchemaDescriptor _schema = new(typeof(ProductSchema), name, version, "{}", hash, []);

        public SchemaDescriptor Derive(Type schemaType, string defaultCulture = "en-US") => _schema;

        public SchemaDescriptor Derive<TSchema>(string defaultCulture = "en-US") where TSchema : class => _schema;
    }

    private sealed class FixedPlanResolver(PlanResolution resolution) : IPlanResolver
    {
        public ValueTask<PlanResolution> ResolveAsync(PlanResolutionRequest request, CancellationToken ct = default) => ValueTask.FromResult(resolution);

        public void Invalidate(string sourceId, string? schemaHash = null)
        {
        }
    }

    private sealed class RecordingPlanResolver : IPlanResolver
    {
        public PlanResolutionRequest? Request { get; private set; }

        public ValueTask<PlanResolution> ResolveAsync(PlanResolutionRequest request, CancellationToken ct = default)
        {
            Request = request;
            return ValueTask.FromResult(PlanResolution.Failed(PlanResolutionFailure.NoPlanAvailable, "SNR-PLAN-004", "missing"));
        }

        public void Invalidate(string sourceId, string? schemaHash = null)
        {
        }
    }

    private sealed class ThrowingPlanResolver : IPlanResolver
    {
        public ValueTask<PlanResolution> ResolveAsync(PlanResolutionRequest request, CancellationToken ct = default) =>
            ValueTask.FromException<PlanResolution>(new InvalidOperationException("resolver failure"));

        public void Invalidate(string sourceId, string? schemaHash = null)
        {
        }
    }
}
