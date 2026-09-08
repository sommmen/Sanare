using Sanare.Abstractions.Plans;

namespace Sanare.Core;

/// <summary>Boundary for obtaining a pre-approved extraction plan for a schema/source pair.</summary>
public interface IExtractionPlanProvider
{
    bool TryGet(string sourceId, Type schemaType, out ExtractionPlan plan);
}
