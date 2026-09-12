using System.Globalization;
using System.Text.RegularExpressions;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;
using Sanare.Core.Schema;

namespace Sanare.Core.Plans;

/// <summary>
/// Structurally validates an <see cref="ExtractionPlan"/> without stopping at the first defect. See
/// docs/features/extraction-plan-model.md ("Validation") for the ten rules implemented here.
/// </summary>
/// <remarks>
/// When request parameters are provided, URL template placeholders are also checked for a matching
/// parameter. Regular-expression locator patterns are compiled with the non-backtracking engine and a
/// one-second timeout.
/// </remarks>
public sealed class PlanValidator : IPlanValidator
{
    private const string StructuralDefectCode = "SNR-PLAN-001";
    private const int MinPages = 1;
    private const int MaxPages = 10_000;
    private const int MinItems = 1;
    private const int MaxItems = 1_000_000;

    /// <summary>The only consent strategy documented anywhere in the spec (docs/sanare/tech-design.md §10.1.1).</summary>
    private static readonly HashSet<string> KnownConsentStrategies = new(StringComparer.OrdinalIgnoreCase) { "cookie" };

    private static readonly HashSet<string> ForbiddenHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Cookie", "Authorization", "Set-Cookie" };

    public PlanValidationResult Validate(
        ExtractionPlan plan,
        SchemaDescriptor? schema = null,
        IReadOnlyDictionary<string, string>? requestParameters = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var defects = new List<PlanDefect>();

        ValidateFields(plan, schema, defects);
        ValidateAcquisition(plan, requestParameters, defects);
        ValidateNotFound(plan, defects);
        ValidateConsent(plan, defects);
        ValidatePagination(plan, defects);
        ValidateSchemaHash(plan, schema, defects);

        return new PlanValidationResult(defects.Count == 0, defects);
    }

    // Rules 2, 3, 4, 5, and the locator/transform slice of rule 1.
    private static void ValidateFields(ExtractionPlan plan, SchemaDescriptor? schema, List<PlanDefect> defects)
    {
        var seenPointers = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < plan.Fields.Count; i++)
        {
            var field = plan.Fields[i];
            var fieldPointer = $"/fields/{i}/pointer";

            if (!IsValidJsonPointer(field.Pointer))
            {
                defects.Add(new PlanDefect(fieldPointer, StructuralDefectCode,
                    $"'{field.Pointer}' is not a valid JSON pointer."));
            }
            else if (schema is not null && !schema.Fields.Any(f => string.Equals(f.JsonPointer, field.Pointer, StringComparison.Ordinal)))
            {
                defects.Add(new PlanDefect(fieldPointer, StructuralDefectCode,
                    $"Pointer '{field.Pointer}' does not exist in schema '{schema.Name}'."));
            }

            if (!seenPointers.Add(field.Pointer))
            {
                defects.Add(new PlanDefect(fieldPointer, StructuralDefectCode,
                    $"Pointer '{field.Pointer}' is used by more than one field."));
            }

            if (field.Locators.Count == 0)
            {
                defects.Add(new PlanDefect($"/fields/{i}/locators", StructuralDefectCode,
                    $"Field '{field.Pointer}' has no locators; at least one is required."));
            }

            for (var l = 0; l < field.Locators.Count; l++)
            {
                var step = field.Locators[l];
                ValidateOperationStep(step.Operation, step.Arguments, plan.Tier, $"/fields/{i}/locators/{l}", defects);
            }

            for (var t = 0; t < field.Transforms.Count; t++)
            {
                var step = field.Transforms[t];
                ValidateOperationStep(step.Operation, step.Arguments, plan.Tier, $"/fields/{i}/transforms/{t}", defects);
            }
        }

        if (schema is not null)
        {
            foreach (var required in schema.Fields.Where(f => f.Required))
            {
                var covered = plan.Fields.Any(f => string.Equals(f.Pointer, required.JsonPointer, StringComparison.Ordinal));
                if (!covered)
                {
                    defects.Add(new PlanDefect(required.JsonPointer, StructuralDefectCode,
                        $"Required schema pointer '{required.JsonPointer}' is not covered by any field plan."));
                }
            }
        }
    }

    // Rules 1 (interaction slice), 8, 9.
    private static void ValidateAcquisition(
        ExtractionPlan plan,
        IReadOnlyDictionary<string, string>? requestParameters,
        List<PlanDefect> defects)
    {
        var acquisition = plan.Acquisition;

        if (!Uri.TryCreate(acquisition.UrlTemplate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            defects.Add(new PlanDefect("/acquisition/urlTemplate", StructuralDefectCode,
                $"'{acquisition.UrlTemplate}' must be an absolute http or https URL."));
        }

        if (requestParameters is not null)
        {
            foreach (Match placeholder in Regex.Matches(acquisition.UrlTemplate, @"\{([^{}]+)\}"))
            {
                var name = placeholder.Groups[1].Value;
                if (!requestParameters.ContainsKey(name))
                {
                    defects.Add(new PlanDefect("/acquisition/urlTemplate", StructuralDefectCode,
                        $"URL template placeholder '{{{name}}}' has no corresponding request parameter."));
                }
            }
        }

        foreach (var header in acquisition.Headers.Keys.Where(key => ForbiddenHeaders.Contains(key)))
        {
            defects.Add(new PlanDefect($"/acquisition/headers/{header}", StructuralDefectCode,
                $"Header '{header}' must not appear in a plan; credentials never live in a plan."));
        }

        for (var i = 0; i < acquisition.Interactions.Count; i++)
        {
            var step = acquisition.Interactions[i];
            ValidateOperationStep(step.Operation, step.Arguments, plan.Tier, $"/acquisition/interactions/{i}", defects);
        }
    }

    // Rule 1 (not-found slice) and tier gating.
    private static void ValidateNotFound(ExtractionPlan plan, List<PlanDefect> defects)
    {
        if (plan.NotFound is not { } notFound)
        {
            return;
        }

        var descriptor = PlanOperationCatalog.Get(notFound.Operation);
        if (!descriptor.AllowedTiers.Contains(plan.Tier))
        {
            defects.Add(new PlanDefect("/notFound/op", StructuralDefectCode,
                $"Operation '{notFound.Operation}' is not allowed for tier '{plan.Tier}'."));
        }

        if (string.IsNullOrWhiteSpace(notFound.Selector))
        {
            defects.Add(new PlanDefect("/notFound/selector", StructuralDefectCode,
                "A not-found predicate requires a non-empty selector."));
        }
    }

    // Rule 7.
    private static void ValidateConsent(ExtractionPlan plan, List<PlanDefect> defects)
    {
        if (plan.Consent is not { } consent)
        {
            return;
        }

        if (!KnownConsentStrategies.Contains(consent.Strategy))
        {
            defects.Add(new PlanDefect("/consent/strategy", StructuralDefectCode,
                $"'{consent.Strategy}' is not a known consent strategy."));
        }
    }

    // Rule 6.
    private static void ValidatePagination(ExtractionPlan plan, List<PlanDefect> defects)
    {
        var pagination = plan.Pagination;

        if (pagination.MaxPages is < MinPages or > MaxPages)
        {
            defects.Add(new PlanDefect("/pagination/maxPages", StructuralDefectCode,
                $"MaxPages ({pagination.MaxPages}) must be between {MinPages} and {MaxPages}."));
        }

        if (pagination.MaxItems is { } maxItems && maxItems is < MinItems or > MaxItems)
        {
            defects.Add(new PlanDefect("/pagination/maxItems", StructuralDefectCode,
                $"MaxItems ({maxItems}) must be between {MinItems} and {MaxItems}."));
        }

        var requiresBrowser = pagination.Strategy is PaginationStrategy.LoadMoreButton or PaginationStrategy.InfiniteScroll;
        if (requiresBrowser && plan.Tier != AcquisitionTier.Browser)
        {
            defects.Add(new PlanDefect("/pagination/strategy", StructuralDefectCode,
                $"Strategy '{pagination.Strategy}' requires tier '{AcquisitionTier.Browser}', but the plan targets '{plan.Tier}'."));
        }
    }

    // Rule 10.
    private static void ValidateSchemaHash(ExtractionPlan plan, SchemaDescriptor? schema, List<PlanDefect> defects)
    {
        if (string.IsNullOrWhiteSpace(plan.SchemaHash))
        {
            defects.Add(new PlanDefect("/schemaHash", StructuralDefectCode, "SchemaHash must not be empty."));
            return;
        }

        if (schema is not null && !string.Equals(plan.SchemaHash, schema.Hash, StringComparison.Ordinal))
        {
            defects.Add(new PlanDefect("/schemaHash", StructuralDefectCode,
                $"SchemaHash '{plan.SchemaHash}' does not match the supplied schema's hash '{schema.Hash}'."));
        }
    }

    // Rule 1: arity, tier, and per-argument-kind heuristics shared by locator/transform/interaction steps.
    private static void ValidateOperationStep(
        PlanOperation operation, IReadOnlyList<string> arguments, AcquisitionTier tier, string pointer, List<PlanDefect> defects)
    {
        var descriptor = PlanOperationCatalog.Get(operation);

        if (arguments.Count < descriptor.MinArguments || arguments.Count > descriptor.MaxArguments)
        {
            var maxDescription = descriptor.MaxArguments == int.MaxValue ? "unbounded" : descriptor.MaxArguments.ToString(CultureInfo.InvariantCulture);
            defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                $"Operation '{operation}' takes between {descriptor.MinArguments} and {maxDescription} argument(s); found {arguments.Count}."));
        }

        if (!descriptor.AllowedTiers.Contains(tier))
        {
            defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                $"Operation '{operation}' is not allowed for tier '{tier}'."));
        }

        if (descriptor.ArgumentKinds.Count == 0)
        {
            return;
        }

        for (var i = 0; i < arguments.Count; i++)
        {
            var kind = descriptor.ArgumentKinds[Math.Min(i, descriptor.ArgumentKinds.Count - 1)];
            var argumentPointer = $"{pointer}/arguments/{i}";
            ValidateArgumentKind(kind, arguments[i], argumentPointer, operation, defects);
        }
    }

    private static void ValidateArgumentKind(
        PlanArgumentKind kind, string argument, string pointer, PlanOperation operation, List<PlanDefect> defects)
    {
        switch (kind)
        {
            case PlanArgumentKind.Int:
                if (!int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                        $"Operation '{operation}' expects an integer argument; found '{argument}'."));
                }

                break;
            case PlanArgumentKind.Pattern:
                try
                {
                    _ = new Regex(argument, RegexOptions.NonBacktracking, TimeSpan.FromSeconds(1));
                }
                catch (Exception ex) when (ex is RegexParseException or NotSupportedException)
                {
                    defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                        $"Operation '{operation}' expects a valid non-backtracking regular expression; '{argument}' does not compile."));
                }

                break;
            case PlanArgumentKind.Culture:
                try
                {
                    _ = CultureInfo.GetCultureInfo(argument);
                }
                catch (CultureNotFoundException)
                {
                    defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                        $"Operation '{operation}' expects a known culture name; '{argument}' is not recognised."));
                }

                break;
            case PlanArgumentKind.Pointer:
                if (!IsValidJsonPointer(argument))
                {
                    defects.Add(new PlanDefect(pointer, StructuralDefectCode,
                        $"Operation '{operation}' expects a valid JSON pointer argument; '{argument}' is not one."));
                }

                break;
            case PlanArgumentKind.Selector:
            case PlanArgumentKind.Literal:
            case PlanArgumentKind.Unit:
            default:
                // No statically checkable shape beyond "is a string": selector dialect, literal content,
                // and unit-symbol vocabularies are runtime/tier-dependent concerns.
                break;
        }
    }

    /// <summary>
    /// True when <paramref name="pointer"/> is a syntactically valid RFC 6901 JSON pointer: either empty
    /// (the whole document) or a sequence of <c>/</c>-prefixed tokens with <c>~</c> only ever followed by
    /// <c>0</c> or <c>1</c>.
    /// </summary>
    private static bool IsValidJsonPointer(string pointer)
    {
        if (pointer.Length == 0)
        {
            return true;
        }

        if (pointer[0] != '/')
        {
            return false;
        }

        for (var i = 0; i < pointer.Length; i++)
        {
            if (pointer[i] == '~' && (i + 1 >= pointer.Length || (pointer[i + 1] != '0' && pointer[i + 1] != '1')))
            {
                return false;
            }
        }

        return true;
    }
}
