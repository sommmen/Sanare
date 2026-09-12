using System.Net;

namespace Sanare.Http.Identity;

/// <summary>
/// A composed, ready-to-send identity for one request: an order-significant header list and any
/// cookie contributions from the per-host jar (docs/features/browsing-identity.md, "Interfaces" &gt;
/// "Outputs"; "Key Behaviors" &gt; "Interface").
/// </summary>
/// <param name="ProfileId">
/// The identity profile that produced this identity (e.g. <c>"AssistantBrowser"</c>). Recorded in
/// run provenance and the compliance report so a site operator's "what was this?" has an answer.
/// </param>
/// <param name="Headers">
/// The ordered header list. Deliberately a list, never a dictionary or map — header order is part
/// of the contract (coherence rule 6) and a dictionary cannot preserve it through the type system.
/// </param>
/// <param name="Cookies">Cookie contributions from the per-host jar, if any are current for the target host.</param>
public sealed record BrowsingIdentity(
    string ProfileId,
    IReadOnlyList<KeyValuePair<string, string>> Headers,
    IReadOnlyList<Cookie> Cookies);
