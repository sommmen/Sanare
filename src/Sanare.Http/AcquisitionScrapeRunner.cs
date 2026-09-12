using System.Globalization;
using System.Runtime.CompilerServices;
using Sanare.Abstractions;
using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Internal;
using Sanare.Abstractions.Quality;
using Sanare.Core;
using Sanare.Core.Acquisition;
using Sanare.Core.Runtime;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Materialization;
using Sanare.Http.Identity;

namespace Sanare.Http;

/// <summary>
/// Composition root that wires live HTTP acquisition and the browsing-identity boundary into the
/// runtime execution path: request → approved plan → identity-bearing network acquisition →
/// deterministic HTML execution → schema validation/materialization → result envelope. This is the
/// network-backed counterpart to <see cref="FixtureScrapeRunner"/> (README.md, "Recommended next
/// step"; docs/features/acquisition-pipeline.md; docs/features/browsing-identity.md).
/// </summary>
/// <remarks>
/// Consent-wall handling is capped at a single retry: if
/// <see cref="IBrowsingIdentityProvider.EvaluateConsent"/> reports a wall on the first fetch, this
/// runner re-composes the identity (picking up the consent cookie the provider just persisted) and
/// re-acquires exactly once. If the wall is still present afterwards, the run terminates with
/// <see cref="ScrapeStatus.ConsentWallBlocked"/> (docs/features/browsing-identity.md, "Constraints" &gt;
/// "Consent retry is capped at one").
/// </remarks>
public sealed class AcquisitionScrapeRunner(
    IExtractionPlanProvider plans,
    IContentAcquirer acquirer,
    IBrowsingIdentityProvider identityProvider,
    ISchemaDeriver schemas,
    IPlanExecutor executor,
    ISchemaValidator validator,
    IDocumentMaterializer materializer) : IScrapeRunner
{
    public async Task<ScrapeResult<TSchema>> RunAsync<TSchema>(ScrapeRequest request, CancellationToken cancellationToken = default)
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
            return CreateResult<TSchema>(failureStatus, null, Array.Empty<FieldHealth>(), validation.Diagnostics, fallbackSourceId, fallbackSchema, started, requestsIssued: 0, origin: ResultOrigin.Network, identityProfileId: null);
        }

        var normalizedRequest = validation.NormalizedRequest!;
        var sourceId = normalizedRequest.SourceId!;
        var culture = normalizedRequest.Culture ?? "en-US";
        var schema = schemas.Derive<TSchema>(culture);

        if (!plans.TryGet(sourceId, typeof(TSchema), out var plan))
        {
            return CreateResult<TSchema>(ScrapeStatus.NoPlanAvailable, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-PLAN-001", DiagnosticSeverity.Error, "No approved plan is available.")], sourceId, schema, started, requestsIssued: 0, origin: ResultOrigin.Network, identityProfileId: null);
        }

        AcquiredContent content;
        var requestsIssued = 0;
        string? identityProfileId = null;
        try
        {
            var identity = identityProvider.GetIdentity(new IdentityRequest(sourceId, normalizedRequest.Url, CultureInfo.GetCultureInfo(culture), NavigationContext.TopLevel, plan.Tier));
            identityProfileId = identity.ProfileId;
            content = await acquirer.AcquireAsync(new AcquisitionRequest(normalizedRequest.Url, sourceId, Tier: plan.Tier, Identity: identity.ToRequestIdentity()), cancellationToken).ConfigureAwait(false);
            requestsIssued++;

            var decision = identityProvider.EvaluateConsent(content, sourceId);
            if (decision.WallDetected)
            {
                if (decision.RetryAttempted)
                {
                    return CreateResult<TSchema>(ScrapeStatus.ConsentWallBlocked, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-ACQ-005", DiagnosticSeverity.Error, $"Consent wall could not be cleared for {content.FinalUrl.Host}.")], sourceId, schema, started, requestsIssued, MapOrigin(content.Origin), identityProfileId);
                }

                var retryIdentity = identityProvider.GetIdentity(new IdentityRequest(sourceId, normalizedRequest.Url, CultureInfo.GetCultureInfo(culture), NavigationContext.TopLevel, plan.Tier));
                identityProfileId = retryIdentity.ProfileId;
                content = await acquirer.AcquireAsync(new AcquisitionRequest(normalizedRequest.Url, sourceId, Tier: plan.Tier, Identity: retryIdentity.ToRequestIdentity()), cancellationToken).ConfigureAwait(false);
                requestsIssued++;

                var retryDecision = identityProvider.EvaluateConsent(content, sourceId);
                if (retryDecision.WallDetected)
                {
                    return CreateResult<TSchema>(ScrapeStatus.ConsentWallBlocked, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-ACQ-005", DiagnosticSeverity.Error, $"Consent wall could not be cleared for {content.FinalUrl.Host}.")], sourceId, schema, started, requestsIssued, MapOrigin(content.Origin), identityProfileId);
                }
            }
        }
        catch (AcquisitionException exception)
        {
            return CreateResult<TSchema>(MapAcquisitionStatus(exception.Code), null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic(exception.Code, DiagnosticSeverity.Error, exception.Message)], sourceId, schema, started, requestsIssued, ResultOrigin.Network, identityProfileId: null);
        }

        var html = content.Charset.GetString(content.Body.Span);

        ExtractionOutcome outcome;
        try
        {
            outcome = executor.Execute(plan, html, schema);
        }
        catch (NotSupportedException exception)
        {
            return CreateResult<TSchema>(ScrapeStatus.PlanInvalid, null, Array.Empty<FieldHealth>(), [.. validation.Diagnostics, new ScrapeDiagnostic("SNR-PLAN-002", DiagnosticSeverity.Error, exception.Message)], sourceId, schema, started, requestsIssued, MapOrigin(content.Origin), identityProfileId);
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
            return CreateResult<TSchema>(ScrapeStatus.SchemaValidationFailed, null, fields, diagnostics, sourceId, schema, started, requestsIssued, MapOrigin(content.Origin), identityProfileId);
        }

        var status = fields.Any(static field => !field.Required && !field.Present)
            ? ScrapeStatus.PartialExtraction
            : ScrapeStatus.Succeeded;
        var payload = materializer.Materialize<TSchema>(outcome.Values);
        return CreateResult(status, payload, fields, diagnostics, sourceId, schema, started, requestsIssued, MapOrigin(content.Origin), identityProfileId);
    }

    public async IAsyncEnumerable<ScrapeItem<TItem>> StreamAsync<TItem>(ScrapeRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TItem : class
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        yield return await Task.FromException<ScrapeItem<TItem>>(
            new NotSupportedException("Streaming requires the M2 pagination engine and is not available in v0.1.")).ConfigureAwait(false);
    }

    private static ResultOrigin MapOrigin(ContentOrigin origin) => origin switch
    {
        ContentOrigin.Network => ResultOrigin.Network,
        ContentOrigin.Cache => ResultOrigin.HttpCache,
        ContentOrigin.Fixture => ResultOrigin.Fixture,
        ContentOrigin.Browser => ResultOrigin.Browser,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Unmapped content origin."),
    };

    private static ScrapeStatus MapAcquisitionStatus(string code) => code switch
    {
        "SNR-FIX-001" => ScrapeStatus.FixtureNotFound,
        "SNR-ACQ-007" => ScrapeStatus.ExtractionFailed,
        "SNR-ACQ-006" => ScrapeStatus.PlanInvalid,
        "SNR-ACQ-009" => ScrapeStatus.InvalidRequest,
        _ => ScrapeStatus.ExtractionFailed,
    };

    private static ScrapeResult<T> CreateResult<T>(ScrapeStatus status, T? payload, IReadOnlyList<FieldHealth> fields, IReadOnlyList<ScrapeDiagnostic> diagnostics, string sourceId, SchemaDescriptor schema, DateTimeOffset started, int requestsIssued, ResultOrigin origin, string? identityProfileId)
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
                RequestsIssued = requestsIssued,
                Origin = origin,
                IdentityProfileId = identityProfileId,
                StartedAt = started,
                Duration = DateTimeOffset.UtcNow - started,
                FixtureIds = origin == ResultOrigin.Fixture ? [sourceId] : Array.Empty<string>(),
            },
            Diagnostics = diagnostics,
        };
}
