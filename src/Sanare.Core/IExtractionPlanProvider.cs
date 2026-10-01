using Sanare.Abstractions.Plans;

namespace Sanare.Core;

/// <summary>Boundary for obtaining a pre-approved extraction plan for a schema/source pair.</summary>
public interface IExtractionPlanProvider
{
    bool TryGet(string sourceId, Type schemaType, out ExtractionPlan plan);

    /// <summary>
    /// Obtains an approved plan and, when the provider resolved it from a versioned repository, its
    /// immutable commit identifier.
    /// </summary>
    bool TryGet(string sourceId, Type schemaType, out ExtractionPlan plan, out string? commitId)
    {
        var found = TryGet(sourceId, schemaType, out plan);
        commitId = null;
        return found;
    }
}
