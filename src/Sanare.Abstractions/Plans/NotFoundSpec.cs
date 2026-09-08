namespace Sanare.Abstractions.Plans;

/// <summary>
/// A single predicate step that, when matched, means the requested resource does not exist (e.g. a
/// deleted product page) rather than that extraction failed. See docs/sanare/tech-design.md §10.1.1
/// (<c>notFound: { "op": "exists", "selector": "..." }</c>) and docs/features/plan-runtime.md
/// ("Terminal predicates") — a match here yields outcome <see cref="ScrapeStatus.SourceNotFound"/> with
/// zero field errors, evaluated before any field locator runs.
/// </summary>
/// <param name="Operation">
/// Typically <see cref="PlanOperation.Exists"/> or <see cref="PlanOperation.NotFoundPredicate"/>; any
/// operation whose descriptor role suits a boolean predicate is structurally permitted and validated by
/// <c>IPlanValidator</c>.
/// </param>
/// <param name="Selector">The selector the predicate is evaluated against.</param>
/// <remarks>No behaviour lives here; evaluating the predicate is <c>plan-runtime</c> scope (M2).</remarks>
public sealed record NotFoundSpec(PlanOperation Operation, string Selector);
