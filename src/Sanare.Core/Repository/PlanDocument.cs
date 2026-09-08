namespace Sanare.Core.Repository;

/// <summary>
/// A canonical plan document read from the script repository at some git reference. See
/// docs/features/script-repository.md ("Interface").
/// </summary>
/// <param name="Json">The plan's canonical JSON text, byte-identical to what was committed.</param>
/// <param name="CommitId">The commit id this document was read at.</param>
/// <param name="Ref">The ref that resolved to <paramref name="CommitId"/> (branch, tag, or the commit id itself).</param>
/// <param name="CommittedAt">The commit's author timestamp.</param>
/// <param name="Message">The full commit message, including the structured trailer block.</param>
public sealed record PlanDocument(
    string Json,
    string CommitId,
    string Ref,
    DateTimeOffset CommittedAt,
    string Message);
