using Sanare.Abstractions.Plans;

namespace Sanare.Core;

/// <summary>In-memory plan source for v0.1 tests and local demonstrations.</summary>
public sealed class InMemoryExtractionPlanProvider : IExtractionPlanProvider
{
    private readonly IReadOnlyDictionary<string, ExtractionPlan> _plans;

    public InMemoryExtractionPlanProvider(IReadOnlyDictionary<string, ExtractionPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        _plans = plans;
    }

    public bool TryGet(string sourceId, Type schemaType, out ExtractionPlan plan) =>
        _plans.TryGetValue(Key(sourceId, schemaType), out plan!);

    public static string Key(string sourceId, Type schemaType) => sourceId + "|" + schemaType.FullName;
}
