# Feature Specifications Overview

> Implementation index for the Sanare technical design.
> Canonical architecture: [Sanare Technical Design](../sanare/tech-design.md)
> Status: active — implementation tracking
> Updated: 2026-09-10

## Status

These specifications are implementation-ready documents generated from the canonical technical design. An `implemented` status records a completed component, `partial` records a deliberately implemented subset, and `draft` remains unimplemented. The
`#` column below is the **only ordering authority**: dependencies expressed elsewhere are explanatory and
must not be used to renumber or reorder work.

## Implementation Order

| # | Feature | Component | Layer | Depends on # | Priority | Status | Specification |
|---:|---------|-----------|-------|--------------|----------|--------|---------------|
| 1 | Scrape API Contracts | `scrape-api-contracts` | L0 | — | P0 | draft | [scrape-api-contracts.md](scrape-api-contracts.md) |
| 2 | Schema Engine | `schema-engine` | L1 | 1 | P0 | implemented | [schema-engine.md](schema-engine.md) |
| 3 | Extraction Plan Model | `extraction-plan-model` | L1 | 1, 2 | P0 | partial | [extraction-plan-model.md](extraction-plan-model.md) |
| 4 | Script Repository | `script-repository` | L1 | 3 | P0 | partial | [script-repository.md](script-repository.md) |
| 5 | Fixture Corpus | `fixture-corpus` | L1 | 1 | P0 | implemented | [fixture-corpus.md](fixture-corpus.md) |
| 6 | Acquisition Pipeline | `acquisition-pipeline` | L2 | 1, 5 | P0 | partial | [acquisition-pipeline.md](acquisition-pipeline.md) |
| 7 | Browsing Identity | `browsing-identity` | L2 | 6 | P0 | draft | [browsing-identity.md](browsing-identity.md) |
| 8 | Browser Tier | `browser-tier` | L2 | 6, 7 | P0 | draft | [browser-tier.md](browser-tier.md) |
| 9 | Plan Runtime | `plan-runtime` | L3 | 2, 3, 6 | P0 | partial | [plan-runtime.md](plan-runtime.md) |
| 10 | Pagination Engine | `pagination-engine` | L3 | 9 | P0 | draft | [pagination-engine.md](pagination-engine.md) |
| 11 | Plan Resolver | `plan-resolver` | L3 | 3, 4 | P0 | partial | [plan-resolver.md](plan-resolver.md) |
| 12 | Authoring Workflow | `authoring-workflow` | L4 | 2, 3, 4, 5, 9, 13 | P0 | draft | [authoring-workflow.md](authoring-workflow.md) |
| 13 | Agent Toolset | `agent-toolset` | L4 | 5, 9 | P0 | draft | [agent-toolset.md](agent-toolset.md) |
| 14 | Quality Evaluator | `quality-evaluator` | L4 | 2, 11 | P0 | draft | [quality-evaluator.md](quality-evaluator.md) |
| 15 | Healing Workflow | `healing-workflow` | L4 | 4, 5, 9, 12, 13, 14 | P0 | draft | [healing-workflow.md](healing-workflow.md) |
| 16 | Observability | `observability` | cross | 1 | P1 | partial | [observability.md](observability.md) |
| 17 | Hosting & Configuration | `hosting-configuration` | cross | 1–16 | P0 | draft | [hosting-configuration.md](hosting-configuration.md) |
| 18 | Lenovo Sample Application | `sample-app-lenovo` | app | 17 | P0 | draft | [sample-app-lenovo.md](sample-app-lenovo.md) |

> Row 12 depends on row 13 despite appearing first because the canonical technical design fixes this
> numbering. Implement row 13's tool contracts before completing row 12; **do not renumber the rows**.

## Execution-Order Rationale

The order follows the technical design's M1–M7 delivery sequence while keeping stable component numbers:

- **M1 — Contracts and offline vertical slice (#1–#5, then #9/#11):** establish public result/status
  contracts, schema derivation, the declarative plan format, git plan history and fixtures. These are the
  durable formats that every later package consumes. The deterministic runtime and resolver then make an
  approved offline plan executable before any agent or browser exists.
- **M2 — HTTP acquisition and polite identity (#6–#7):** add bounded network access, caching, retries,
  rate limiting, robots policy and a stable honest identity. This proves ordinary scraping without paying
  the browser cost.
- **M3 — Browser and pagination (#8, #10):** add the explicitly gated Playwright fallback and first-class
  page enumeration once the network and runtime boundaries are stable.
- **M4 — Authoring (#13, then #12):** expose the fixed read-only offline tool surface, then build the Agent
  Framework graph that proposes, dry-runs, judges and commits plans. Although the immutable table numbers
  authoring as #12 and tools as #13, implementation completes #13 first.
- **M5 — Quality and healing (#14–#15):** persist run evidence, detect field and item-count decay, coalesce
  triggers, diagnose, patch and regression-validate. Healing intentionally comes after authoring because
  it reuses authoring validation/commit executors and the same tool surface.
- **M6 — Composition and Lenovo proof (#16–#18):** wire telemetry throughout, register and validate the
  complete system through standard DI, then prove the two Lenovo scenarios end to end. Observability is
  P1 for first-user value but is placed before hosting so the final composition root can register all
  activity sources, meters, health checks and alerts once.
- **M7 — Hardening (cross-cutting):** run the full offline/fixture/chaos/security suite, verify native git
  packaging and browser installation, exercise rollback and retention, and publish the library packages.
  M7 does not introduce another feature spec; it closes the non-functional criteria in #1–#17.

## Architectural Through-Line

1. The consumer supplies a **typed schema** through #1/#2.
2. #11 resolves an approved **declarative extraction plan** defined by #3 and versioned by #4.
3. #6–#8 acquire evidence at the cheapest successful tier; #9 interprets the plan and #10 enumerates
   pages.
4. If there is no plan, #12 uses #13 against #5's offline evidence to author and validate one.
5. #14 evaluates result health; #15 creates a minimal, regression-tested repair when health decays.
6. #16 makes every step diagnosable; #17 composes the library safely; #18 proves the system against the
   requested Lenovo lister and product-detail pages.

## Non-Goals Shared by All Features

- No impersonation of ChatGPT or any named crawler.
- No residential proxy rotation, CAPTCHA solving, TLS/JA3 spoofing, fingerprint randomisation, or
  authentication/paywall bypass. (Robots.txt `Disallow` bypass is not excluded here — it is the ordinary
  per-source default; `RespectRobots` opts a source into enforcement. See DR-016.)
- No unconstrained model-generated executable code. The declarative operation allow-list is the primary
  security boundary; optional compiled plans remain disabled and double-gated.
- No relational database or hosted scraper service in this scope. State is git + content-addressed files
  + append-only telemetry, and hosting belongs to the consuming application.
