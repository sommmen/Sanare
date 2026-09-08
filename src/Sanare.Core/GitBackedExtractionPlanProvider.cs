using Sanare.Abstractions.Plans;
using Sanare.Core.Resolution;
using Sanare.Core.Schema;

namespace Sanare.Core;

/// <summary>
/// Adapts <see cref="Resolution.IPlanResolver"/> to the existing <see cref="IExtractionPlanProvider"/>
/// boundary, so <see cref="FixtureScrapeRunner"/> and the <c>IScrapeRunner</c> contract need no change
/// while plans are now read from the persistent, git-backed script repository instead of an in-memory
/// dictionary. See docs/features/plan-resolver.md and docs/features/script-repository.md.
/// </summary>
public sealed class GitBackedExtractionPlanProvider(IPlanResolver resolver, ISchemaDeriver schemas) : IExtractionPlanProvider
{
    public bool TryGet(string sourceId, Type schemaType, out ExtractionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(sourceId);
        ArgumentNullException.ThrowIfNull(schemaType);

        var schema = schemas.Derive(schemaType);
        var request = new PlanResolutionRequest(sourceId, schema.Name, schema.Version, schema.Hash);
        var resolution = resolver.ResolveAsync(request).AsTask().GetAwaiter().GetResult();
        if (resolution.IsResolved)
        {
            plan = resolution.Plan!.Plan;
            return true;
        }

        plan = null!;
        return false;
    }
}
