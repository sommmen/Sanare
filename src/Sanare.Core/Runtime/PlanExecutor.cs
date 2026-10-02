using System.Globalization;
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

            if (!coercer.TryCoerce(transformed, field, out var value, out var coercionError))
            {
                values[field.JsonPointer] = null;
                coercionFailedFields.Add(field.JsonPointer);
                diagnostics.Add(new ScrapeDiagnostic("SNR-SCH-005", DiagnosticSeverity.Error, coercionError!, field.JsonPointer));
                continue;
            }

            values[field.JsonPointer] = value;
        }

        var requiredPresent = !diagnostics.Any(static diagnostic => diagnostic.Code is "SNR-SCH-004" or "SNR-SCH-005" && diagnostic.Severity == DiagnosticSeverity.Error);
        return new ExtractionOutcome(values, diagnostics, coercionFailedFields, requiredPresent);
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
            default:
                throw new NotSupportedException($"Plan operation '{locator.Operation}' is outside the v0.1 HTML runtime subset.");
        }
    }

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
                default:
                    error = $"Plan transform '{transform.Operation}' is outside the v0.1 HTML runtime subset.";
                    return false;
            }
        }

        error = null;
        return true;
    }
}
