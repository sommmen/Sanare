using Sanare.Abstractions.Telemetry;

namespace Sanare.Core.Observability.Alerting;

/// <summary>The stable operational alert catalog.</summary>
public static class AlertRuleSet
{
    public static readonly AlertRule SourceBlocked = new("SourceBlocked", AlertSeverity.Critical);
    public static readonly AlertRule SourceEmpty = new("SourceEmpty", AlertSeverity.Critical);
    public static readonly AlertRule FieldDecay = new("FieldDecay", AlertSeverity.High);
    public static readonly AlertRule HealFailed = new("HealFailed", AlertSeverity.High);
    public static readonly AlertRule HealRegression = new("HealRegression", AlertSeverity.High);
    public static readonly AlertRule AuthoringFailing = new("AuthoringFailing", AlertSeverity.Medium);
    public static readonly AlertRule LlmCostSpike = new("LlmCostSpike", AlertSeverity.Medium);
    public static readonly AlertRule BudgetNearLimit = new("BudgetNearLimit", AlertSeverity.Medium);
    public static readonly AlertRule BudgetExhausted = new("BudgetExhausted", AlertSeverity.High);
    public static readonly AlertRule ChallengePausedTooLong = new("ChallengePausedTooLong", AlertSeverity.High);
    public static readonly AlertRule PlanStale = new("PlanStale", AlertSeverity.Low);
    public static readonly AlertRule StateRootPressure = new("StateRootPressure", AlertSeverity.High);
    public static readonly AlertRule AwaitingApprovalBacklog = new("AwaitingApprovalBacklog", AlertSeverity.Low);
}
