namespace Sanare.Http.Resilience;

/// <summary>The context a Sanare process is running in, which gates operator-only surfaces.</summary>
public enum ExecutionMode
{
    /// <summary>Normal operation against live hosts.</summary>
    Live,

    /// <summary>Deterministic replay from the fixture corpus; no socket is opened.</summary>
    OfflineFixture,

    /// <summary>An unattended automation context — CI, a scheduler — where no operator is present.</summary>
    Unattended,
}

/// <summary>The outcome of an operator's manual challenge hand-off.</summary>
/// <param name="Resolved">Whether the supervised probe came back clean and the circuit was closed.</param>
/// <param name="Reason">A short operator-facing explanation of the outcome.</param>
public sealed record ChallengeHandoffResult(bool Resolved, string Reason);

/// <summary>
/// The operator-only escape hatch from <see cref="BreakerState.ChallengePaused"/>
/// (docs/features/acquisition-pipeline.md, "Key Behaviors" &gt; "Retry and circuit breaking", route 2).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This interface has no automated caller anywhere in <c>src/</c>, and an architecture test
/// asserts that it never gains one.</strong> It is reachable only from an explicit CLI or API action
/// gated like any other operator-only surface. Making it callable from the pipeline would turn a
/// deliberate human decision into an automated block-evasion loop, which is exactly what DR-006 and
/// NG-1–NG-3 forbid.
/// </para>
/// <para>
/// What the implementation does is open a real, visible browser — the same binary the browser tier uses,
/// not a stealth build — against the source's URL under the operator's own network path, and wait for the
/// operator to browse normally until the host's risk scoring relaxes. Nothing here solves a CAPTCHA,
/// spoofs a fingerprint, or forges an identity; it is a human doing what a human may do, then telling the
/// pipeline the block is likely cleared. The pipeline then runs exactly one supervised probe and closes
/// the circuit only if that probe is clean.
/// </para>
/// </remarks>
public interface IChallengeHandoff
{
    /// <summary>
    /// Opens a supervised hand-off session for <paramref name="sourceId"/>, then runs one probe request
    /// and closes the circuit if it is clean.
    /// </summary>
    /// <param name="sourceId">The paused source to hand off.</param>
    /// <param name="ct">Cancels the hand-off.</param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the process is running under <see cref="ExecutionMode.OfflineFixture"/> or
    /// <see cref="ExecutionMode.Unattended"/>, where no operator is present to supervise.
    /// </exception>
    ValueTask<ChallengeHandoffResult> OpenAsync(string sourceId, CancellationToken ct = default);
}
