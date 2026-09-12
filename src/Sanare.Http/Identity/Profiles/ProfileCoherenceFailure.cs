namespace Sanare.Http.Identity.Profiles;

/// <summary>
/// One coherence-rule violation for a profile, named rather than a bare <see cref="bool"/> so an
/// <c>SNR-ID-001</c> diagnostic can name the rule and the offending header
/// (docs/features/browsing-identity.md, "Error Handling").
/// </summary>
/// <param name="Rule">The 1-based coherence-rule number that failed.</param>
/// <param name="ProfileId">The profile that failed the rule.</param>
/// <param name="Header">The offending header name, when the failure is header-specific.</param>
/// <param name="Detail">A human-readable explanation of the failure.</param>
public sealed record ProfileCoherenceFailure(int Rule, string ProfileId, string? Header, string Detail)
{
    public override string ToString() => Header is null
        ? $"[{ProfileId}] rule {Rule}: {Detail}"
        : $"[{ProfileId}] rule {Rule} ({Header}): {Detail}";
}
