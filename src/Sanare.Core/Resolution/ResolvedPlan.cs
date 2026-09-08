using Sanare.Abstractions.Plans;

namespace Sanare.Core.Resolution;

/// <summary>An approved plan resolved from the script repository. See docs/features/plan-resolver.md ("Outputs").</summary>
public sealed record ResolvedPlan(
    ExtractionPlan Plan,
    string CommitId,
    string ApprovalTag);
