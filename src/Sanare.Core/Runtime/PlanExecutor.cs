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
        for (var index = 0; index < field.Locators.Count; index++)
        {
            var locator = field.Locators[index];
            var value = locator.Operation switch
            {
                PlanOperation.SelectFirst or PlanOperation.Text when locator.Arguments.Count == 1 => document.SelectText(locator.Arguments[0]),
                PlanOperation.Attribute when locator.Arguments.Count == 2 => document.SelectAttribute(locator.Arguments[0], locator.Arguments[1]),
                _ => throw new NotSupportedException($"Plan operation '{locator.Operation}' is outside the v0.1 HTML runtime subset."),
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                if (index > 0)
                {
                    diagnostics.Add(new ScrapeDiagnostic("SNR-EXT-001", DiagnosticSeverity.Info, "A fallback locator succeeded.", field.Pointer));
                }

                return value;
            }
        }

        return null;
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
