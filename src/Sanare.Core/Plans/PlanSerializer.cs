using System.Globalization;
using System.Text;
using System.Text.Json;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;

namespace Sanare.Core.Plans;

/// <summary>
/// Canonical <see cref="ExtractionPlan"/> reader/writer. Writes deterministic, byte-stable JSON (fixed key
/// order, 2-space indentation, LF line endings, trailing newline) so that git history for a plan reflects
/// only real semantic changes. See docs/features/extraction-plan-model.md ("Canonical serialization").
/// </summary>
/// <remarks>
/// This is the minimal slice needed to persist/read plans for <c>script-repository</c>/<c>plan-resolver</c>.
/// Malformed documents surface as <see cref="PlanSerializationException"/> during read/write. Structural
/// validation of an already-deserialized plan (field coverage, operation arity/tier gating, pagination
/// bounds, etc.) is performed separately by <see cref="IPlanValidator"/>/<see cref="PlanValidator"/>; this
/// serializer does not invoke it.
/// </remarks>
public sealed class PlanSerializer : IPlanSerializer
{
    public ExtractionPlan Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var planVersion = GetInt(root, "planVersion");
            if (planVersion > ExtractionPlan.CurrentPlanVersion || planVersion < ExtractionPlan.MinimumReadablePlanVersion)
            {
                throw new PlanSerializationException(
                    $"SNR-PLAN-002: Plan version {planVersion} is outside the readable window " +
                    $"[{ExtractionPlan.MinimumReadablePlanVersion}, {ExtractionPlan.CurrentPlanVersion}].");
            }

            if (planVersion == ExtractionPlan.MinimumReadablePlanVersion && ExtractionPlan.MinimumReadablePlanVersion < ExtractionPlan.CurrentPlanVersion)
            {
                return PlanVersionUpgrader.TryUpgrade(root, out var upgraded, out var failure)
                    ? upgraded!
                    : throw new PlanSerializationException($"{failure!.Code}: {failure.Message}");
            }

            return new ExtractionPlan
            {
                PlanVersion = planVersion,
                SourceId = GetString(root, "sourceId"),
                SchemaName = GetString(root, "schemaName"),
                SchemaVersion = GetInt(root, "schemaVersion"),
                SchemaHash = GetString(root, "schemaHash"),
                Culture = GetString(root, "culture"),
                Tier = ParseEnum<AcquisitionTier>(GetString(root, "tier")),
                Acquisition = ReadAcquisition(root.GetProperty("acquisition")),
                NotFound = root.TryGetProperty("notFound", out var notFound) && notFound.ValueKind != JsonValueKind.Null
                    ? ReadNotFound(notFound)
                    : null,
                Consent = root.TryGetProperty("consent", out var consent) && consent.ValueKind != JsonValueKind.Null
                    ? ReadConsent(consent)
                    : null,
                Pagination = root.TryGetProperty("pagination", out var pagination) && pagination.ValueKind != JsonValueKind.Null
                    ? ReadPagination(pagination)
                    : PaginationSpec.None,
                Root = root.TryGetProperty("root", out var rootLocator) && rootLocator.ValueKind != JsonValueKind.Null
                    ? rootLocator.GetString()
                    : null,
                Fields = [.. root.GetProperty("fields").EnumerateArray().Select(ReadField)],
                Provenance = ReadProvenance(root.GetProperty("provenance")),
            };
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new PlanSerializationException("SNR-PLAN-001: The plan document is not valid JSON or is missing a required field.", exception);
        }
    }

    public string WriteCanonical(ExtractionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, IndentSize = 2, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("planVersion", plan.PlanVersion);
            writer.WriteString("sourceId", plan.SourceId);
            writer.WriteString("schemaName", plan.SchemaName);
            writer.WriteNumber("schemaVersion", plan.SchemaVersion);
            writer.WriteString("schemaHash", plan.SchemaHash);
            writer.WriteString("culture", plan.Culture);
            writer.WriteString("tier", CamelCase(plan.Tier.ToString()));
            WriteAcquisition(writer, plan.Acquisition);

            if (plan.NotFound is { } notFound)
            {
                writer.WriteStartObject("notFound");
                writer.WriteString("op", CamelCase(notFound.Operation.ToString()));
                writer.WriteString("selector", notFound.Selector);
                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("notFound");
            }

            if (plan.Consent is { } consent)
            {
                writer.WriteStartObject("consent");
                writer.WriteString("strategy", consent.Strategy);
                if (consent.Name is not null)
                {
                    writer.WriteString("name", consent.Name);
                }
                else
                {
                    writer.WriteNull("name");
                }

                writer.WriteEndObject();
            }
            else
            {
                writer.WriteNull("consent");
            }

            WritePagination(writer, plan.Pagination);

            if (plan.Root is not null)
            {
                writer.WriteString("root", plan.Root);
            }
            else
            {
                writer.WriteNull("root");
            }

            writer.WriteStartArray("fields");
            foreach (var field in plan.Fields.OrderBy(static field => field.Pointer, StringComparer.Ordinal))
            {
                WriteField(writer, field);
            }

            writer.WriteEndArray();
            WriteProvenance(writer, plan.Provenance);
            writer.WriteEndObject();
        }

        var bytes = stream.ToArray();
        var text = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n", StringComparison.Ordinal);
        return text.EndsWith('\n') ? text : text + "\n";
    }

    internal static AcquisitionSpec ReadAcquisition(JsonElement element)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element.TryGetProperty("headers", out var headersElement) && headersElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var header in headersElement.EnumerateObject())
            {
                headers[header.Name] = header.Value.GetString() ?? string.Empty;
            }
        }

        var interactions = element.TryGetProperty("interactions", out var interactionsElement) && interactionsElement.ValueKind == JsonValueKind.Array
            ? interactionsElement.EnumerateArray().Select(ReadStep).Select(static step => new InteractionStep(step.Operation, step.Arguments)).ToArray()
            : [];

        return new AcquisitionSpec(
            ParseEnum<AcquisitionMethod>(GetString(element, "method")),
            GetString(element, "urlTemplate"),
            headers,
            element.TryGetProperty("waitFor", out var waitFor) && waitFor.ValueKind != JsonValueKind.Null ? waitFor.GetString() : null,
            interactions);
    }

    private static void WriteAcquisition(Utf8JsonWriter writer, AcquisitionSpec acquisition)
    {
        writer.WriteStartObject("acquisition");
        writer.WriteString("method", CamelCase(acquisition.Method.ToString()));
        writer.WriteString("urlTemplate", acquisition.UrlTemplate);
        writer.WriteStartObject("headers");
        foreach (var header in acquisition.Headers.OrderBy(static header => header.Key, StringComparer.Ordinal))
        {
            writer.WriteString(header.Key, header.Value);
        }

        writer.WriteEndObject();
        if (acquisition.WaitFor is not null)
        {
            writer.WriteString("waitFor", acquisition.WaitFor);
        }
        else
        {
            writer.WriteNull("waitFor");
        }

        writer.WriteStartArray("interactions");
        foreach (var interaction in acquisition.Interactions)
        {
            WriteStep(writer, interaction.Operation, interaction.Arguments);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static NotFoundSpec ReadNotFound(JsonElement element) =>
        new(ParseEnum<PlanOperation>(GetString(element, "op")), GetString(element, "selector"));

    internal static ConsentSpec ReadConsent(JsonElement element) =>
        new(GetString(element, "strategy"), element.TryGetProperty("name", out var name) && name.ValueKind != JsonValueKind.Null ? name.GetString() : null);

    internal static PaginationSpec ReadPagination(JsonElement element) =>
        new(
            ParseEnum<PaginationStrategy>(GetString(element, "strategy")),
            element.TryGetProperty("nextSelector", out var next) && next.ValueKind != JsonValueKind.Null ? next.GetString() : null,
            element.TryGetProperty("maxPages", out var maxPages) ? maxPages.GetInt32() : 1,
            element.TryGetProperty("itemKey", out var itemKey) && itemKey.ValueKind != JsonValueKind.Null ? itemKey.GetString() : null,
            element.TryGetProperty("maxItems", out var maxItems) && maxItems.ValueKind != JsonValueKind.Null ? maxItems.GetInt32() : null);

    private static void WritePagination(Utf8JsonWriter writer, PaginationSpec pagination)
    {
        writer.WriteStartObject("pagination");
        writer.WriteString("strategy", CamelCase(pagination.Strategy.ToString()));
        if (pagination.NextSelector is not null)
        {
            writer.WriteString("nextSelector", pagination.NextSelector);
        }
        else
        {
            writer.WriteNull("nextSelector");
        }

        writer.WriteNumber("maxPages", pagination.MaxPages);
        if (pagination.ItemKey is not null)
        {
            writer.WriteString("itemKey", pagination.ItemKey);
        }
        else
        {
            writer.WriteNull("itemKey");
        }

        if (pagination.MaxItems is { } maxItems)
        {
            writer.WriteNumber("maxItems", maxItems);
        }

        writer.WriteEndObject();
    }

    private static FieldPlan ReadField(JsonElement element)
    {
        var primary = ReadStep(element.GetProperty("primaryLocator"));
        var fallback = ReadStep(element.GetProperty("fallbackLocator"));
        return new FieldPlan(
            GetString(element, "pointer"),
            element.TryGetProperty("required", out var required) && required.GetBoolean(),
            GetString(element, "type"),
            [.. element.GetProperty("locators").EnumerateArray().Select(ReadStep).Select(static step => new LocatorStep(step.Operation, step.Arguments))],
            [.. element.GetProperty("transforms").EnumerateArray().Select(ReadStep).Select(static step => new TransformStep(step.Operation, step.Arguments))])
        {
            PrimaryLocator = new LocatorStep(primary.Operation, primary.Arguments),
            FallbackLocator = new LocatorStep(fallback.Operation, fallback.Arguments),
        };
    }

    private static void WriteField(Utf8JsonWriter writer, FieldPlan field)
    {
        writer.WriteStartObject();
        writer.WriteString("pointer", field.Pointer);
        writer.WriteBoolean("required", field.Required);
        writer.WriteStartArray("locators");
        foreach (var locator in field.Locators)
        {
            WriteStep(writer, locator.Operation, locator.Arguments);
        }

        writer.WriteEndArray();
        writer.WritePropertyName("primaryLocator");
        WriteStep(writer, field.PrimaryLocator.Operation, field.PrimaryLocator.Arguments);
        writer.WritePropertyName("fallbackLocator");
        WriteStep(writer, field.FallbackLocator.Operation, field.FallbackLocator.Arguments);
        writer.WriteStartArray("transforms");
        foreach (var transform in field.Transforms)
        {
            WriteStep(writer, transform.Operation, transform.Arguments);
        }

        writer.WriteEndArray();
        writer.WriteString("type", field.Type);
        writer.WriteEndObject();
    }

    internal static (PlanOperation Operation, IReadOnlyList<string> Arguments) ReadStep(JsonElement element) =>
        (
            ParseEnum<PlanOperation>(GetString(element, "op")),
            element.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Array
                ? [.. arguments.EnumerateArray().Select(static argument => argument.GetString() ?? string.Empty)]
                : []
        );

    private static void WriteStep(Utf8JsonWriter writer, PlanOperation operation, IReadOnlyList<string> arguments)
    {
        writer.WriteStartObject();
        writer.WriteString("op", CamelCase(operation.ToString()));
        writer.WriteStartArray("arguments");
        foreach (var argument in arguments)
        {
            writer.WriteStringValue(argument);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    internal static PlanProvenance ReadProvenance(JsonElement element) =>
        new(
            GetString(element, "authoredBy"),
            GetString(element, "model"),
            element.TryGetProperty("attempts", out var attempts) ? attempts.GetInt32() : 0,
            element.TryGetProperty("fixtureIds", out var fixtureIds) && fixtureIds.ValueKind == JsonValueKind.Array
                ? [.. fixtureIds.EnumerateArray().Select(static id => id.GetString() ?? string.Empty)]
                : [],
            element.TryGetProperty("score", out var score) ? score.GetDouble() : 0d,
            DateTimeOffset.Parse(GetString(element, "authoredAt"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    private static void WriteProvenance(Utf8JsonWriter writer, PlanProvenance provenance)
    {
        writer.WriteStartObject("provenance");
        writer.WriteString("authoredBy", provenance.AuthoredBy);
        writer.WriteString("model", provenance.Model);
        writer.WriteNumber("attempts", provenance.Attempts);
        writer.WriteStartArray("fixtureIds");
        foreach (var fixtureId in provenance.FixtureIds)
        {
            writer.WriteStringValue(fixtureId);
        }

        writer.WriteEndArray();
        writer.WriteNumber("score", Math.Round(provenance.Score, 4, MidpointRounding.AwayFromZero));
        writer.WriteString("authoredAt", provenance.AuthoredAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    internal static string GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? throw new PlanSerializationException($"SNR-PLAN-001: '{propertyName}' must not be null.")
            : throw new PlanSerializationException($"SNR-PLAN-001: '{propertyName}' is missing or not a string.");

    internal static int GetInt(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : throw new PlanSerializationException($"SNR-PLAN-001: '{propertyName}' is missing or not a number.");

    internal static TEnum ParseEnum<TEnum>(string value) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new PlanSerializationException($"SNR-PLAN-001: '{value}' is not a recognised {typeof(TEnum).Name}.");

    private static string CamelCase(string value) =>
        value.Length == 0 ? value : string.Create(value.Length, value, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
}
