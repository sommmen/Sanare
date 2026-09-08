# Session: explore-initial-spark

> 2026-09-06 · Type: Exploration

## Type: Exploration

## Context

A .NET product-data aggregation system needs structured product data (listings + full specification
tables) from vendor and retailer sites that expose no usable public API — concretely Lenovo's NL
catalogue, and bol.com next. The request: a reusable C# library, built on the Microsoft Agent Framework,
where a consumer supplies a site and a schema and the framework produces, runs, versions, and repairs the
scraping logic itself.

## Key Points

- The framework input is `(site/URL, C# schema type)`; the output is JSON/objects conforming to that schema.
- LLM involvement should be at **authoring and healing** time, not per page — otherwise catalogue-scale
  crawling becomes token-cost-bound and non-deterministic.
- Normal networked scraping is strongly preferred; Playwright is an escalation tier for pages that
  genuinely require JS execution or interaction (consent walls, infinite scroll, client-rendered specs).
- The browsing identity should look like an ordinary/assistant browser fetch (ChatGPT-style browsing agent)
  because such agents are widely allowlisted; only *bare minimum* detection circumvention is in scope.
- Everything as typed as the problem allows: generic `RunAsync<TSchema>`, generated JSON Schema from the
  POCO, typed result envelopes — while accepting that the extraction layer itself is dynamic.
- Generated scripts must be versioned with real history and merge semantics → embed a git repository on
  disk rather than inventing a bespoke version table.
- Validation must run against locally captured production pages (`tablet-lister.html`, `pc-data.html`,
  `product-lister.json`, …) so iteration never depends on hitting upstream.
- A periodic evaluator must notice degraded returns (missing fields, type failures, empty pages, a cookie
  wall appearing) and dispatch a self-heal run.
- Must handle rate limits, cache results, and avoid overloading or being blocked by the target.
- Pagination is a first-class requirement, not an afterthought.

## Problem Definition

Hand-maintained scrapers fail **silently and expensively**. A layout change turns into `null` fields, not
an exception, so bad data flows into the aggregated catalogue; the fix requires a human devtools session
against the live site; and repeated debugging traffic is exactly what triggers blocking. There is no
diffable history of extraction logic, so regressions cannot be bisected or rolled back.

## Demand Signal

Strong and concrete: an existing aggregation system, two named Lenovo URLs with precise extraction goals
(full tablet product list; product info + the entire spec table), a named second source (bol.com), and a
stated absence of APIs for both. This is an in-hand requirement, not a hypothetical market.

## Decisions

1. **LLM authors a deterministic artifact.** Per-page LLM extraction is rejected for cost/determinism;
   the agent instead emits a script that is compiled, tested, committed, and then run token-free.
2. **Fixtures are the test suite.** Real captured pages on disk are the substrate for authoring, healing,
   scoring, and regression tests. Live fetches only capture/refresh fixtures.
3. **Git on disk for script versioning.** Gives history, blame, branching for heal attempts, merge, and
   rollback out of the box.
4. **Tiered extraction.** Structured data (JSON-LD / embedded JSON / internal JSON endpoints) → HTML
   parsing → Playwright, with escalation only on failure and a recorded rationale.
5. **Blend-in politeness, not evasion.** Realistic assistant-browser identity, robots awareness, per-host
   rate limiting, caching, backoff. No CAPTCHA solving, no proxy rotation, no auth bypass.
6. **Heal runs must not regress.** A repaired script must pass against both the new fixture and the
   retained historical fixtures before it can be promoted.

## Open Questions

- Generated-script representation: Roslyn-compiled C#, a declarative extraction DSL, or a hybrid?
- LibGit2Sharp in-process vs. a `git` binary for the on-disk repository?
- How much isolation do generated scripts need (separate process, resource caps, no ambient filesystem)?
- Should heal promotion be autonomous or gated by human approval — per source, presumably configurable.
- Fixture retention policy and PII redaction.

## Research Needed

- Exact Microsoft Agent Framework C# package IDs, workflow API, structured-output API, and DI/hosting
  patterns (dispatched to a research pass; feeds §7.1 of the tech design).
- Lenovo NL page structure: JSON-LD, internal listing endpoints, pagination mechanism, consent wall.
- bol.com robots/ToS posture before adding it as a reference source.

## Raw Notes

- Sample app must demonstrate both a **lister** (all tablets, paginated) and a **detail** page (product
  info + complete spec table) so both the collection and deep-extraction paths are exercised.
- "Perhaps the app should ship git for a local on-disk repo" — user's own suggestion; adopted.
- "As typed as possible, but obviously dynamic" — resolve by making the *contract* typed (generic schema
  parameter, typed envelopes, typed diagnostics) while the *extraction plan* stays data-driven.
