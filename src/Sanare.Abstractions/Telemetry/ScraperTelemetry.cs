namespace Sanare.Abstractions.Telemetry;

/// <summary>Stable OpenTelemetry names exposed by the Sanare library.</summary>
public static class ScraperTelemetry
{
    public const string ActivitySourceName = "Sanare";
    public const string MeterName = "Sanare";
    public const string AgentActivitySourceName = "Experimental.Microsoft.Agents.AI";
    public const string ChatActivitySourceName = "Experimental.Microsoft.Extensions.AI";
}
