using System.Text.Json;
using Sanare.Abstractions;
using Sanare.Abstractions.Plans;

namespace Sanare.Core.Plans;

/// <summary>
/// Upgrades a <c>planVersion == <see cref="ExtractionPlan.MinimumReadablePlanVersion"/></c> document to an
/// in-memory <see cref="ExtractionPlan"/> at <see cref="ExtractionPlan.CurrentPlanVersion"/> (DR-002's
/// <c>N-1</c> read-compatibility window). Only the in-memory graph is upgraded; the on-disk bytes are
/// untouched. See docs/features/extraction-plan-model.md ("Implementation Plan", T3).
/// </summary>
internal static class PlanVersionUpgrader
{
    /// <summary>
    /// Attempts to upgrade a version-1 plan document. Fails, rather than truncating or fabricating a
    /// candidate, when any field's legacy <c>locators[]</c> array does not have exactly two entries
    /// (index 0 → <see cref="FieldPlan.PrimaryLocator"/>, index 1 → <see cref="FieldPlan.FallbackLocator"/>).
    /// </summary>
    public static bool TryUpgrade(JsonElement root, out ExtractionPlan? plan, out PlanDefect? failure)
    {
        var fields = new List<FieldPlan>();
        foreach (var fieldElement in root.GetProperty("fields").EnumerateArray())
        {
            var locators = fieldElement.GetProperty("locators");
            var locatorCount = locators.ValueKind == JsonValueKind.Array ? locators.GetArrayLength() : 0;
            if (locatorCount != 2)
            {
                plan = null;
                failure = new PlanDefect(
                    "/fields",
                    "SNR-PLAN-002",
                    $"Version {ExtractionPlan.MinimumReadablePlanVersion} field '{PlanSerializer.GetString(fieldElement, "pointer")}' " +
                    $"has {locatorCount} locator(s); an upgrade to version {ExtractionPlan.CurrentPlanVersion} " +
                    "requires exactly two (primary, fallback).");
                return false;
            }

            var steps = locators.EnumerateArray().Select(PlanSerializer.ReadStep).ToArray();
            fields.Add(new FieldPlan(
                PlanSerializer.GetString(fieldElement, "pointer"),
                fieldElement.TryGetProperty("required", out var required) && required.GetBoolean(),
                PlanSerializer.GetString(fieldElement, "type"),
                [.. steps.Select(static step => new LocatorStep(step.Operation, step.Arguments))],
                [.. fieldElement.GetProperty("transforms").EnumerateArray().Select(PlanSerializer.ReadStep).Select(static step => new TransformStep(step.Operation, step.Arguments))])
            {
                PrimaryLocator = new LocatorStep(steps[0].Operation, steps[0].Arguments),
                FallbackLocator = new LocatorStep(steps[1].Operation, steps[1].Arguments),
            });
        }

        plan = new ExtractionPlan
        {
            PlanVersion = ExtractionPlan.CurrentPlanVersion,
            SourceId = PlanSerializer.GetString(root, "sourceId"),
            SchemaName = PlanSerializer.GetString(root, "schemaName"),
            SchemaVersion = PlanSerializer.GetInt(root, "schemaVersion"),
            SchemaHash = PlanSerializer.GetString(root, "schemaHash"),
            Culture = PlanSerializer.GetString(root, "culture"),
            Tier = PlanSerializer.ParseEnum<AcquisitionTier>(PlanSerializer.GetString(root, "tier")),
            Acquisition = PlanSerializer.ReadAcquisition(root.GetProperty("acquisition")),
            NotFound = root.TryGetProperty("notFound", out var notFound) && notFound.ValueKind != JsonValueKind.Null
                ? PlanSerializer.ReadNotFound(notFound)
                : null,
            Consent = root.TryGetProperty("consent", out var consent) && consent.ValueKind != JsonValueKind.Null
                ? PlanSerializer.ReadConsent(consent)
                : null,
            Pagination = root.TryGetProperty("pagination", out var pagination) && pagination.ValueKind != JsonValueKind.Null
                ? PlanSerializer.ReadPagination(pagination)
                : PaginationSpec.None,
            Root = root.TryGetProperty("root", out var rootLocator) && rootLocator.ValueKind != JsonValueKind.Null
                ? rootLocator.GetString()
                : null,
            Fields = fields,
            Provenance = PlanSerializer.ReadProvenance(root.GetProperty("provenance")),
        };
        failure = null;
        return true;
    }
}
