using System.Runtime.CompilerServices;
using Sanare.Abstractions;
using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Internal;
using Sanare.Abstractions.Quality;
using Sanare.Core.Fixtures;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Materialization;

namespace Sanare.Core;

/// <summary>
/// Minimal offline engine composition root: request → approved plan → fixture content → deterministic
/// HTML execution → schema validation/materialization → result envelope. Network acquisition and plan
/// authoring intentionally belong to later milestones.
/// </summary>
public sealed class FixtureScrapeRunner(
    IExtractionPlanProvider plans,
    IFixtureContentProvider fixtures,
    ISchemaDeriver schemas,
    IPlanExecutor executor,
    ISchemaValidator validator,
    IDocumentMaterializer materializer) : IScrapeRunner
{
    public Task<ScrapeResult<TSchema>> RunAsync<TSchema>(ScrapeRequest request, CancellationToken cancellationToken = default)
        where TSchema : class
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var started = DateTimeOffset.UtcNow;

        var validation = RequestValidator.Validate(request);
        if (validation.FailureStatus is { } failureStatus)
        {
            var fallbackSourceId = request.SourceId ?? "unresolved";
            var fallbackSchema = schemas.Derive<TSchema>("en-US");
            return Task.FromResult(CreateResult<TSchema>(failureStatus, null, Array.Empty<FieldHealth>(), validation.Diagnostics, fallbackSourceId, fallbackSchema, started));
        }

        var normalizedRequest = validation.NormalizedRequest!;
        var sourceId = normalizedRequest.SourceId!;
        var schema = schemas.Derive<TSchema>(normalizedRequest.Culture ?? "en-US");

        if (!plans.TryGet(sourceId, typeof(TSchema), out var plan))
        {
            return Task.FromResult(CreateResult<TSchema>(ScrapeStatus.NoPlanAvailable, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-PLAN-001", DiagnosticSeverity.Error, "No approved plan is available.")], sourceId, schema, started));
        }

        if (!fixtures.TryGet(sourceId, normalizedRequest.Url, out var html))
        {
            return Task.FromResult(CreateResult<TSchema>(ScrapeStatus.FixtureNotFound, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-FIX-001", DiagnosticSeverity.Error, "No fixture is available for this request.")], sourceId, schema, started));
        }

        ExtractionOutcome outcome;
        try
        {
            outcome = executor.Execute(plan, html, schema);
        }
        catch (NotSupportedException exception)
        {
            return Task.FromResult(CreateResult<TSchema>(ScrapeStatus.PlanInvalid, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-PLAN-002", DiagnosticSeverity.Error, exception.Message)], sourceId, schema, started));
        }

        var diagnostics = validation.Diagnostics.Concat(outcome.Diagnostics).ToArray();
        var schemaValidation = validator.Validate(schema, outcome.Values);
        var fields = schema.Fields.Select(field =>
        {
            var present = outcome.Values.TryGetValue(field.JsonPointer, out var value) && value is not null;
            var coercionSucceeded = present && !outcome.CoercionFailedFields.Contains(field.JsonPointer);
            return new FieldHealth(field.JsonPointer, field.Required, present, coercionSucceeded, present ? 0d : 1d);
        }).ToArray();

        if (!schemaValidation.IsValid || outcome.CoercionFailedFields.Count > 0)
        {
            return Task.FromResult(CreateResult<TSchema>(ScrapeStatus.SchemaValidationFailed, null, fields, diagnostics, sourceId, schema, started));
        }

        var status = fields.Any(static field => !field.Required && !field.Present)
            ? ScrapeStatus.PartialExtraction
            : ScrapeStatus.Succeeded;
        var payload = materializer.Materialize<TSchema>(outcome.Values);
        return Task.FromResult(CreateResult(status, payload, fields, diagnostics, sourceId, schema, started));
    }

    public async IAsyncEnumerable<ScrapeItem<TItem>> StreamAsync<TItem>(ScrapeRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        yield return await Task.FromException<ScrapeItem<TItem>>(
            new NotSupportedException("Streaming requires the M2 pagination engine and is not available in v0.1.")).ConfigureAwait(false);
    }

    private static ScrapeResult<T> CreateResult<T>(ScrapeStatus status, T? payload, IReadOnlyList<FieldHealth> fields, IReadOnlyList<ScrapeDiagnostic> diagnostics, string sourceId, SchemaDescriptor schema, DateTimeOffset started)
        where T : class =>
        new()
        {
            Status = status,
            Payload = payload,
            Quality = new QualityReport
            {
                Completeness = fields.Count == 0 ? 0d : fields.Count(static field => field.Present && field.CoercionSucceeded) / (double)fields.Count,
                Fields = fields,
                UnmappedFields = Array.Empty<string>(),
                ItemCount = payload is null ? 0 : 1,
                MeetsThreshold = status is ScrapeStatus.Succeeded or ScrapeStatus.PartialExtraction,
            },
            Provenance = new RunProvenance
            {
                RunId = Guid.NewGuid().ToString("N"),
                SourceId = sourceId,
                SchemaHash = schema.Hash,
                Tier = AcquisitionTier.Html,
                PagesFetched = payload is null ? 0 : 1,
                RequestsIssued = 0,
                Origin = ResultOrigin.Fixture,
                StartedAt = started,
                Duration = DateTimeOffset.UtcNow - started,
                FixtureIds = [sourceId],
            },
            Diagnostics = diagnostics,
        };
}
