namespace Sanare.Core.Resolution;

/// <summary>Why plan resolution did not produce a usable plan. See docs/features/plan-resolver.md ("Outputs").</summary>
public enum PlanResolutionFailure
{
    /// <summary>No approved plan exists for the (sourceId, schemaName, schemaVersion) triple.</summary>
    NoPlanAvailable,

    /// <summary>An approved plan exists but its schemaHash differs from the request's (SNR-PLAN-003).</summary>
    SchemaDrift,

    /// <summary>The plan document at the approved commit could not be read (SNR-PLAN-001).</summary>
    PlanInvalid,
}
