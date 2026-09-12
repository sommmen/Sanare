using Sanare.Abstractions.Plans;
using Sanare.Core.Schema;

namespace Sanare.Core.Plans;

/// <summary>
/// Structurally validates an <see cref="ExtractionPlan"/> before it is written to disk or executed. See
/// docs/features/extraction-plan-model.md ("Validation") for the full rule set.
/// </summary>
public interface IPlanValidator
{
    /// <summary>
    /// Validates <paramref name="plan"/>, optionally cross-checking it against <paramref name="schema"/>
    /// (pointer coverage/membership and schema-hash agreement). All applicable defects are reported
    /// together — validation never stops at the first one.
    /// </summary>
    /// <param name="plan">The plan to validate.</param>
    /// <param name="schema">
    /// The schema the plan targets, or <see langword="null"/> to skip the schema-dependent checks
    /// (rules 2's membership check, rule 3, and rule 10's hash-equality check).
    /// </param>
    /// <param name="requestParameters">
    /// Bound request parameters. When supplied, every <c>{placeholder}</c> in the URL template must
    /// have a corresponding key.
    /// </param>
    PlanValidationResult Validate(
        ExtractionPlan plan,
        SchemaDescriptor? schema = null,
        IReadOnlyDictionary<string, string>? requestParameters = null);
}
