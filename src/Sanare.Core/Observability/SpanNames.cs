namespace Sanare.Core.Observability;

/// <summary>Stable names for Sanare activities.</summary>
public static class SpanNames
{
    public const string RunExecute = "sanare.run.execute";
    public const string PlanResolve = "sanare.plan.resolve";
    public const string AcquisitionFetch = "sanare.acquisition.fetch";
    public const string BrowserNavigate = "sanare.browser.navigate";
    public const string ExtractionExecute = "sanare.extraction.execute";
    public const string SchemaValidate = "sanare.schema.validate";
    public const string AuthoringPrefix = "sanare.authoring.";
    public const string HealingPrefix = "sanare.healing.";
}
