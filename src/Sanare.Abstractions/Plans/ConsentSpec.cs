namespace Sanare.Abstractions.Plans;

/// <summary>
/// Names the consent/cookie-wall handling strategy a plan expects, e.g.
/// <c>{ "strategy": "cookie", "name": "OptanonAlertBoxClosed" }</c> (docs/sanare/tech-design.md §10.1.1).
/// A match on the associated detection signature yields outcome
/// <see cref="ScrapeStatus.ConsentWallBlocked"/> (docs/features/plan-runtime.md, "Terminal predicates").
/// </summary>
/// <param name="Strategy">
/// One of the known consent strategies (validated against a closed set by <c>IPlanValidator</c>, per
/// docs/features/extraction-plan-model.md rule 7); <c>"cookie"</c> is the only strategy named in the
/// documented example.
/// </param>
/// <param name="Name">
/// The strategy's required argument — for <c>"cookie"</c>, the consent cookie's name. Optional because
/// not every strategy necessarily needs one; validity of the combination is a <c>plan-runtime</c>/
/// <c>browsing-identity</c> concern (M2), not enforced by the type shape itself.
/// </param>
/// <remarks>No behaviour lives here; applying the strategy is <c>browsing-identity</c> scope (M2).</remarks>
public sealed record ConsentSpec(string Strategy, string? Name = null);
