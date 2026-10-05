using Sanare.Abstractions.Plans;
using Sanare.Core.Schema;

namespace Sanare.Core.Runtime;

/// <summary>Executes a validated extraction plan against already-acquired HTML content.</summary>
public interface IPlanExecutor
{
    ExtractionOutcome Execute(ExtractionPlan plan, string html, SchemaDescriptor schema);

    /// <summary>Executes a collection plan once for every item selected by its root locator.</summary>
    ExtractionOutcome[] ExecuteMany(ExtractionPlan plan, string content, SchemaDescriptor schema);
}
