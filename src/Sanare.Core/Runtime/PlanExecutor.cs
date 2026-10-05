using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sanare.Abstractions.Diagnostics;
using Sanare.Abstractions.Plans;
using Sanare.Core.Runtime.Documents;
using Sanare.Core.Schema;
using Sanare.Core.Schema.Coercion;

namespace Sanare.Core.Runtime;

/// <summary>
/// Deterministic HTML interpreter for the minimal v0.1 operation subset. Acquisition, browser operations,
/// collections, and operations outside this subset are intentionally deferred to their owning M2 features.
/// </summary>
public sealed class PlanExecutor(ITypeCoercer coercer) : IPlanExecutor
{
    public ExtractionOutcome Execute(ExtractionPlan plan, string html, SchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(schema);

        var document = new HtmlDocument(html);
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        var diagnostics = new List<ScrapeDiagnostic>();
        var coercionFailedFields = new HashSet<string>(StringComparer.Ordinal);
        var fields = schema.Fields.ToDictionary(static field => field.JsonPointer, StringComparer.Ordinal);

        foreach (var fieldPlan in plan.Fields.OrderBy(static field => field.Pointer, StringComparer.Ordinal))
        {
            if (!fields.TryGetValue(fieldPlan.Pointer, out var field))
            {
                diagnostics.Add(new ScrapeDiagnostic("SNR-EXT-001", DiagnosticSeverity.Warning, "Plan field is not part of the schema.", fieldPlan.Pointer));
                continue;
            }

            var raw = Locate(document, fieldPlan, diagnostics);
            if (raw is null)
            {
                values[field.JsonPointer] = null;
                if (field.Required || fieldPlan.Required)
                {
                    diagnostics.Add(new ScrapeDiagnostic("SNR-SCH-004", DiagnosticSeverity.Error, "A required field is missing.", field.JsonPointer));
                }

                continue;
            }

            if (!TryTransform(raw, fieldPlan.Transforms, out var transformed, out var transformError))
            {
                values[field.JsonPointer] = null;
                diagnostics.Add(new ScrapeDiagnostic("SNR-EXT-001", DiagnosticSeverity.Warning, transformError!, field.JsonPointer));
                continue;
            }

            var coercion = coercer.Coerce(
                transformed,
                field,
                new CoercionContext(Culture: CultureInfo.InvariantCulture, DocumentBaseUri: new Uri(plan.Acquisition.UrlTemplate, UriKind.Absolute)));
            if (!coercion.Success)
            {
                values[field.JsonPointer] = null;
                coercionFailedFields.Add(field.JsonPointer);
                diagnostics.Add(new ScrapeDiagnostic("SNR-SCH-005", DiagnosticSeverity.Error, coercion.FailureReason!, field.JsonPointer));
                continue;
            }

            values[field.JsonPointer] = coercion.Value is not null
                ? coercion.Value.Deserialize(field.ClrType)
                : coercion.Value;
        }

        var requiredPresent = !diagnostics.Any(static diagnostic => diagnostic.Code is "SNR-SCH-004" or "SNR-SCH-005" && diagnostic.Severity == DiagnosticSeverity.Error);
        return new ExtractionOutcome(values, diagnostics, coercionFailedFields, requiredPresent);
    }

    /// <summary>Executes a collection plan against each HTML or JSON item selected by <see cref="ExtractionPlan.Root"/>.</summary>
    public ExtractionOutcome[] ExecuteMany(ExtractionPlan plan, string content, SchemaDescriptor schema)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(plan.Root))
        {
            throw new InvalidOperationException("A collection plan must declare a root locator.");
        }

        var jsonRoot = plan.Root.StartsWith('$');
        var itemContents = jsonRoot
            ? SelectJsonItems(content, plan.Root)
            : new HtmlDocument(content).SelectAllOuterHtml(plan.Root);
        var maxItems = plan.Pagination.MaxItems;
        if (maxItems is not null)
        {
            itemContents = itemContents.Take(maxItems.Value).ToArray();
        }

        // A collection schema has item pointers below its collection property. A top-level schema
        // (such as a listing card) uses every field unchanged for each selected root item.
        var collectionPointer = schema.CollectionPointer;
        var itemPlan = collectionPointer is null
            ? plan with { Root = null }
            : plan with
            {
                Root = null,
                Fields = plan.Fields.Where(field => IsCollectionField(field.Pointer, collectionPointer)).Select(field => Relativize(field, collectionPointer)).ToArray(),
            };
        var itemSchema = collectionPointer is null
            ? schema
            : schema with
            {
                Fields = schema.Fields.Where(field => IsCollectionField(field.JsonPointer, collectionPointer)).Select(field => Relativize(field, collectionPointer)).ToArray(),
                CollectionPointer = null,
            };
        return itemContents.Select(item => Execute(itemPlan, jsonRoot ? $"<script class=\"sanare-json-item\">{item}</script>" : $"<div class=\"sanare-html-item\">{item}</div>", itemSchema)).ToArray();
    }

    private static bool IsCollectionField(string pointer, string collectionPointer) =>
        pointer.StartsWith(collectionPointer + "/*/", StringComparison.Ordinal);

    private static FieldPlan Relativize(FieldPlan field, string collectionPointer) =>
        field with { Pointer = field.Pointer[(collectionPointer.Length + 2)..] };

    private static FieldDescriptor Relativize(FieldDescriptor field, string collectionPointer) =>
        field with { JsonPointer = field.JsonPointer[(collectionPointer.Length + 2)..] };

    /// <summary>
    /// Resolves the JSON document a <c>$</c>-rooted plan selects over. Content that is already JSON is
    /// used as-is. Otherwise the content is treated as markup and each <c>&lt;script&gt;</c> element is
    /// searched for an embedded JSON object literal — the "JSON island" pattern used by sites that
    /// server-render their data into a page rather than exposing an API.
    /// </summary>
    /// <remarks>
    /// This scan is deliberately site-agnostic: it recognises the shape <c>… = { … }</c> inside a script
    /// body rather than any particular variable name, so no site-specific knowledge lives in this generic
    /// runtime. Candidate islands are returned in document order and the caller picks the first whose
    /// content satisfies the plan's root path, which keeps selection driven by the plan rather than by a
    /// heuristic guess here.
    /// </remarks>
    private static IEnumerable<string> ResolveJsonCandidates(string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            yield return content;
            yield break;
        }

        foreach (var script in new HtmlDocument(content).SelectAllTextContent("script"))
        {
            if (TryReadJsonObject(script, out var island))
            {
                yield return island;
            }
        }
    }

    /// <summary>
    /// Extracts the first balanced JSON object literal from a script body, honouring string literals and
    /// escapes so that a brace inside a string cannot end the object early.
    /// </summary>
    private static bool TryReadJsonObject(string script, out string json)
    {
        json = string.Empty;
        var start = script.IndexOf('{');
        if (start < 0)
        {
            return false;
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = start; index < script.Length; index++)
        {
            var character = script[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inString)
            {
                if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    inString = true;
                    break;
                case '{':
                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        json = script[start..(index + 1)];
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> SelectJsonItems(string content, string path)
    {
        foreach (var candidate in ResolveJsonCandidates(content))
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                if (ResolveJsonArray(document.RootElement, path) is { Count: > 0 } items)
                {
                    return items.Select(static item => item.GetRawText()).ToArray();
                }
            }
            catch (JsonException)
            {
                // A script body that is not valid JSON is simply not this plan's island; keep looking.
            }
        }

        return [];
    }

    private static IReadOnlyList<JsonElement>? ResolveJsonArray(JsonElement root, string path)
    {
        if (!path.StartsWith("$.", StringComparison.Ordinal))
        {
            return null;
        }

        IReadOnlyList<JsonElement> current = [root];
        foreach (var segment in path[2..].Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bracket = segment.IndexOf('[');
            var property = bracket < 0 ? segment : segment[..bracket];
            var selector = bracket < 0 ? null : segment[bracket..];
            if (selector is not null && (!selector.EndsWith(']') || selector.Length < 3))
            {
                return null;
            }

            var next = new List<JsonElement>();
            foreach (var element in current)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value))
                {
                    continue;
                }

                if (selector is null)
                {
                    next.Add(value);
                }
                else if (value.ValueKind == JsonValueKind.Array && selector == "[*]")
                {
                    next.AddRange(value.EnumerateArray());
                }
                else if (value.ValueKind == JsonValueKind.Array && int.TryParse(selector[1..^1], CultureInfo.InvariantCulture, out var index) && index >= 0 && index < value.GetArrayLength())
                {
                    next.Add(value[index]);
                }
            }

            if (next.Count == 0)
            {
                return null;
            }

            current = next;
        }

        return current.All(static element => element.ValueKind == JsonValueKind.Array)
            ? current.SelectMany(static element => element.EnumerateArray()).ToArray()
            : current;
    }

    private static string? Locate(HtmlDocument document, FieldPlan field, List<ScrapeDiagnostic> diagnostics)
    {
        for (var candidateStart = 0; candidateStart < field.Locators.Count;)
        {
            var candidateEnd = candidateStart + 1;
            while (candidateEnd < field.Locators.Count && !IsDocumentLocator(field.Locators[candidateEnd].Operation))
            {
                candidateEnd++;
            }

            var value = LocateFromDocument(document, field.Locators[candidateStart]);
            for (var index = candidateStart + 1; value is not null && index < candidateEnd; index++)
            {
                value = LocateFromValue(value, field.Locators[index]);
                if (value is null && field.Locators[index].Operation == PlanOperation.JsonPath)
                {
                    diagnostics.Add(new ScrapeDiagnostic("SNR-EXT-001", DiagnosticSeverity.Warning, "JSONPath locator did not produce a value.", field.Pointer));
                }
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (candidateStart > 0)
                {
                    diagnostics.Add(new ScrapeDiagnostic("SNR-EXT-001", DiagnosticSeverity.Info, "A fallback locator succeeded.", field.Pointer));
                }

                return value;
            }

            candidateStart = candidateEnd;
        }

        return null;
    }

    // JsonPath, RegexCapture, Index, Split, Text, and Attribute consume the preceding value;
    // a repeated SelectFirst, SelectAll, or XPath starts an alternative from the document.
    private static bool IsDocumentLocator(PlanOperation operation) => operation is PlanOperation.SelectFirst or PlanOperation.SelectAll or PlanOperation.XPath;

    private static string? LocateFromDocument(HtmlDocument document, LocatorStep locator) => locator.Operation switch
    {
        PlanOperation.SelectFirst or PlanOperation.Text when locator.Arguments.Count == 1 => document.SelectText(locator.Arguments[0]),
        PlanOperation.Attribute when locator.Arguments.Count == 2 => document.SelectAttribute(locator.Arguments[0], locator.Arguments[1]),
        PlanOperation.XPath when locator.Arguments.Count == 1 => document.SelectXPath(locator.Arguments[0]),
        // Zero-arg Html as a first step has no selector to apply, so it yields the whole document's markup.
        PlanOperation.Html when locator.Arguments.Count == 0 => document.OuterHtml,
        _ => throw new NotSupportedException($"Plan operation '{locator.Operation}' is outside the v0.1 HTML runtime subset."),
    };

    private static string? LocateFromValue(string value, LocatorStep locator)
    {
        switch (locator.Operation)
        {
            case PlanOperation.Text when locator.Arguments.Count == 1:
                return new HtmlDocument(value).SelectText(locator.Arguments[0]);
            case PlanOperation.Attribute when locator.Arguments.Count == 2:
                return new HtmlDocument(value).SelectAttribute(locator.Arguments[0], locator.Arguments[1]);
            // A chained Html step re-interprets the already-extracted string as HTML; the value itself
            // does not change, since the preceding step already produced the raw markup/text to carry forward.
            case PlanOperation.Html when locator.Arguments.Count == 0:
                return value;
            case PlanOperation.RegexCapture when locator.Arguments.Count is 1 or 2:
                return RegexCapture(value, locator.Arguments);
            case PlanOperation.JsonPath when locator.Arguments.Count == 1:
                return JsonPath(value, locator.Arguments[0]);
            default:
                throw new NotSupportedException($"Plan operation '{locator.Operation}' is outside the v0.1 HTML runtime subset.");
        }
    }

    /// <summary>
    /// Evaluates the supported JSONPath subset: <c>$.a.b</c>, <c>$.a[0].b</c>, and <c>$.a[*].b</c>.
    /// Wildcard paths return the first scalar match. Invalid paths, malformed JSON, and missing values return
    /// <c>null</c> so a failed extraction is reported through the normal field diagnostics.
    /// </summary>
    private static string? JsonPath(string value, string path)
    {
        if (!TryParseJsonPath(path, out var segments))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            IEnumerable<JsonElement> current = [document.RootElement];

            foreach (var segment in segments)
            {
                current = segment.Index switch
                {
                    null => current.Where(element => element.ValueKind == JsonValueKind.Object)
                        .SelectMany(element => element.TryGetProperty(segment.Property, out var property)
                            ? new[] { property }
                            : Enumerable.Empty<JsonElement>()),
                    -1 => current.Where(element => element.ValueKind == JsonValueKind.Object)
                        .SelectMany(element => element.TryGetProperty(segment.Property, out var property) && property.ValueKind == JsonValueKind.Array
                            ? property.EnumerateArray()
                            : Enumerable.Empty<JsonElement>()),
                    _ => current.Where(element => element.ValueKind == JsonValueKind.Object)
                        .SelectMany(element => element.TryGetProperty(segment.Property, out var property) && property.ValueKind == JsonValueKind.Array && segment.Index.Value < property.GetArrayLength()
                            ? new[] { property[segment.Index.Value] }
                            : Enumerable.Empty<JsonElement>()),
                };
            }

            var match = current.FirstOrDefault();
            return match.ValueKind is JsonValueKind.Undefined or JsonValueKind.Object or JsonValueKind.Array
                ? null
                : match.ValueKind == JsonValueKind.String ? match.GetString() : match.GetRawText();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryParseJsonPath(string path, out List<JsonPathSegment> segments)
    {
        segments = [];
        if (string.IsNullOrEmpty(path) || path[0] != '$')
        {
            return false;
        }

        for (var index = 1; index < path.Length;)
        {
            if (path[index++] != '.')
            {
                return false;
            }

            var propertyStart = index;
            while (index < path.Length && (char.IsLetterOrDigit(path[index]) || path[index] == '_' || path[index] == '-'))
            {
                index++;
            }

            if (propertyStart == index)
            {
                return false;
            }

            var property = path[propertyStart..index];
            int? arrayIndex = null;
            if (index < path.Length && path[index] == '[')
            {
                var closeIndex = path.IndexOf(']', ++index);
                if (closeIndex < 0)
                {
                    return false;
                }

                var indexText = path[index..closeIndex];
                if (indexText == "*")
                {
                    arrayIndex = -1;
                }
                else if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedIndex) || parsedIndex < 0)
                {
                    return false;
                }
                else
                {
                    arrayIndex = parsedIndex;
                }

                index = closeIndex + 1;
            }

            segments.Add(new JsonPathSegment(property, arrayIndex));
        }

        return segments.Count > 0;
    }

    private sealed record JsonPathSegment(string Property, int? Index);

    /// <summary>Matches <paramref name="arguments"/>[0] as a non-backtracking pattern (1-second timeout,
    /// matching <c>PlanValidator</c>'s authoring-time compile check) against <paramref name="value"/> and
    /// returns the optional group at <paramref name="arguments"/>[1] (default: the whole match, group 0).
    /// A non-matching pattern yields <c>null</c> rather than throwing.</summary>
    private static string? RegexCapture(string value, IReadOnlyList<string> arguments)
    {
        var groupIndex = 0;
        if (arguments.Count == 2 && !int.TryParse(arguments[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out groupIndex))
        {
            return null;
        }

        Match match;
        try
        {
            match = Regex.Match(value, arguments[0], RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
        }
        catch (Exception exception) when (exception is RegexParseException or RegexMatchTimeoutException or NotSupportedException)
        {
            return null;
        }

        if (!match.Success || groupIndex >= match.Groups.Count)
        {
            return null;
        }

        var group = match.Groups[groupIndex];
        return group.Success ? group.Value : null;
    }

    private static bool TryTransform(string input, IReadOnlyList<TransformStep> transforms, out string output, out string? error)
    {
        output = input;
        foreach (var transform in transforms)
        {
            switch (transform.Operation)
            {
                case PlanOperation.Trim:
                    output = output.Trim();
                    break;
                case PlanOperation.CollapseWhitespace:
                    output = TextNormalizer.Normalize(output);
                    break;
                case PlanOperation.StripCurrency:
                    output = output.Trim().TrimStart('$', '€', '£', '¥', '₹').Trim();
                    break;
                case PlanOperation.StripUnit when transform.Arguments.Count == 1 && output.TrimEnd().EndsWith(transform.Arguments[0], StringComparison.OrdinalIgnoreCase):
                    output = output.TrimEnd()[..^transform.Arguments[0].Length].Trim();
                    break;
                case PlanOperation.Split when transform.Arguments.Count == 1:
                    output = string.Join("\n", output.Split(transform.Arguments[0]));
                    break;
                case PlanOperation.Index when transform.Arguments.Count == 1 && int.TryParse(transform.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index):
                    var values = output.Split('\n');
                    if (index < 0 || index >= values.Length)
                    {
                        error = $"Index '{index}' is outside the split value range.";
                        return false;
                    }

                    output = values[index];
                    break;
                case PlanOperation.Concat:
                    output = string.Join(transform.Arguments.Count == 1 ? transform.Arguments[0] : string.Empty, output.Split('\n'));
                    break;
                case PlanOperation.Coalesce:
                    output = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? string.Empty;
                    break;
                case PlanOperation.Exists:
                    output = (!string.IsNullOrWhiteSpace(output)).ToString().ToLowerInvariant();
                    break;
                case PlanOperation.MapEnum when transform.Arguments.Count >= 2 && transform.Arguments.Count % 2 == 0:
                    for (var argumentIndex = 0; argumentIndex < transform.Arguments.Count; argumentIndex += 2)
                    {
                        if (string.Equals(output, transform.Arguments[argumentIndex], StringComparison.OrdinalIgnoreCase))
                        {
                            output = transform.Arguments[argumentIndex + 1];
                            break;
                        }
                    }

                    break;
                case PlanOperation.ParseInt:
                    if (!int.TryParse(output, NumberStyles.Integer, GetCulture(transform.Arguments), out var integer))
                    {
                        error = $"'{output}' is not a valid integer.";
                        return false;
                    }

                    output = integer.ToString(CultureInfo.InvariantCulture);
                    break;
                case PlanOperation.ParseDecimal:
                    if (!decimal.TryParse(output, NumberStyles.Number, GetCulture(transform.Arguments), out var decimalValue))
                    {
                        error = $"'{output}' is not a valid decimal.";
                        return false;
                    }

                    output = decimalValue.ToString(CultureInfo.InvariantCulture);
                    break;
                case PlanOperation.ParseBool:
                    if (!bool.TryParse(output, out var boolean))
                    {
                        error = $"'{output}' is not a valid Boolean.";
                        return false;
                    }

                    output = boolean.ToString().ToLowerInvariant();
                    break;
                case PlanOperation.Html:
                    output = HtmlToReadableText(output);
                    break;
                default:
                    error = $"Plan transform '{transform.Operation}' is outside the v0.1 HTML runtime subset.";
                    return false;
            }
        }

        error = null;
        return true;
    }

    private static CultureInfo GetCulture(IReadOnlyList<string> arguments) =>
        arguments.Count == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(arguments[0]);

    /// <summary>Flattens HTML list and paragraph content to normalized, newline-separated readable text.</summary>
    private static string HtmlToReadableText(string html)
    {
        var text = Regex.Replace(html, "</(?:li|p|div|br|tr|h[1-6])\\s*>", "\n", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        return TextNormalizer.Normalize(new HtmlDocument(text).TextContent.Replace("\n", " ", StringComparison.Ordinal));
    }
}
