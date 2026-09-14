# Sanare

Sanare is a draft-first .NET library for schema-driven, self-healing product-data extraction. It uses the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) to author, version, execute, evaluate, and repair extraction plans while preferring efficient network and structured-data acquisition before escalating to Playwright.

## Documentation

- [Technical design](docs/sanare/tech-design.md) — canonical architecture, operational decisions, and delivery milestones.
- [Feature specifications](docs/features/overview.md) — ordered implementation index and component specifications.
- [Product idea](ideas/sanare/draft.md) — goals, reference scenarios, and MVP boundaries.

## Scope

The initial reference scenarios cover Lenovo tablet-list pagination and product/spec-table extraction. The design supports typed schema outputs, offline fixture-based validation, Git-backed plan history, source-level LLM budgets, adaptive request pacing and caching, and OpenTelemetry/Aspire-compatible observability.

## Current status: v0.1 foundation

This repository currently implements a **v0.1 foundation**: the project skeleton, the core domain/engine contracts, a deterministic offline execution path, the complete local Git-backed approved-plan repository with an approval-tag resolver over it, the on-disk fixture corpus, and the governed HTTP acquisition pipeline with deterministic browsing identity composition, consent-wall handling, and compliance reporting. Both runners now exist side by side: `FixtureScrapeRunner` stays fixture-backed for offline work, while `AcquisitionScrapeRunner` wires live governed HTTP acquisition and the identity boundary into the same request → plan → execute → materialize path. A `Sanare.Browser` project now composes the double-opt-in browser tier (gate, pool, page scope, wait strategy, interactions, resource blocking, cookie hand-off, and the DR-014 manual challenge hand-off) behind `AcquisitionPipelineFactory.CreateBrowser(...)` and a `TieredContentAcquirer` that never falls back between tiers. Its tagged real-Chromium integration suite serves deterministic local test pages through Kestrel, exercising rendered acquisition, interactions, resource blocking, pool lifecycle, and manual challenge hand-off. Plan authoring/healing and pagination remain unimplemented — see [What's intentionally unimplemented](#whats-intentionally-unimplemented).

### Project structure

| Project | Purpose |
| --- | --- |
| `src/Sanare.Abstractions` | Core domain and engine contracts: `ScrapeRequest`/`ScrapeResult`/`ScrapeStatus`, the `IScrapeRunner` execution interface, extraction-plan models, the closed `PlanOperation` vocabulary, schema attributes, diagnostics, and quality/provenance types. No infrastructure dependencies. |
| `src/Sanare.Core` | The v0.1 engine and local infrastructure: schema derivation/coercion/materialization, an AngleSharp-backed HTML document adapter, a narrow `PlanExecutor`, `FixtureScrapeRunner`, the Git-backed plan repository and resolver, observability primitives, and an on-disk fixture corpus with mandatory redaction and DR-011 retention. |
| `src/Sanare.Http` | The governed HTTP acquisition pipeline and browsing-identity components: `GovernedContentAcquirer`, per-host pacing, `robots.txt` enforcement, retries and circuit breaking, conditional caching, deterministic profile headers, profile-coherence validation, host-scoped consent cookies, consent and wall classification, credential-free compliance reports, and the network-backed `AcquisitionScrapeRunner`. |
| `tests/Sanare.Abstractions.Tests` | Contract tests for request validation, the operation catalog, diagnostics sanitization, and the approved public API surface. |
| `tests/Sanare.Core.Tests` | Unit and integration-style tests for schema execution, fixture capture/redaction/normalization/retention, Git-backed plan storage and resolution, plan serialization/validation, observability, and deterministic zero-socket offline replay. |
| `tests/Sanare.Http.Tests` | Unit, architecture, and API-surface approval tests for the governed acquirer, robots, politeness, resilience, caching, redirects, discovery, browsing identities, consent handling, and the acquisition runner. |

### Engine execution path (v0.1)

Two runners share the same pipeline shape and differ only in how content is acquired.

`FixtureScrapeRunner` — deterministic, offline, zero-socket:

```
ScrapeRequest
  → RequestValidator            (URL/source-id/freshness/culture validation, MaxItems clamping)
  → schema derivation            (reflect TSchema → FieldPlan-compatible schema shape)
  → IExtractionPlanProvider      (looks up a hand-authored ExtractionPlan for the source)
  → IFixtureContentProvider      (looks up deterministic fixture HTML; stand-in for live acquisition)
  → IPlanExecutor                (executes locator/transform steps against the HTML document)
  → schema validation/materialization
  → ScrapeResult<TSchema>        (status, payload, diagnostics, provenance)
```

`AcquisitionScrapeRunner` — live, governed, identity-bearing:

```
ScrapeRequest
  → RequestValidator            (URL/source-id/freshness/culture validation, MaxItems clamping)
  → schema derivation            (reflect TSchema → FieldPlan-compatible schema shape)
  → IExtractionPlanProvider      (looks up the approved ExtractionPlan for the source)
  → IBrowsingIdentityProvider    (composes a coherent per-source identity for the plan's tier)
  → IContentAcquirer             (governed HTTP: robots, pacing, retries, breaker, cache)
                                 (consent walls trigger exactly one re-composed retry)
  → IPlanExecutor                (executes locator/transform steps against the HTML document)
  → schema validation/materialization
  → ScrapeResult<TSchema>        (status, payload, diagnostics, provenance)
```

The minimal runner example remains usable with hand-authored plans through `InMemoryExtractionPlanProvider` and deterministic content through `InMemoryFixtureContentProvider`. The repository also includes the local Git-backed approved-plan repository/resolver and an independent on-disk fixture corpus.

`HttpContentAcquirer` supports GET-only HTTPS requests by default, streamed 16 MiB response limits, response-header normalization, content-type validation, charset detection, fixture capture, and zero-socket fixture replay. `GovernedContentAcquirer` wraps that transport with the policy layer — per-host token buckets and politeness gaps, `robots.txt` enforcement evaluated before any token is consumed, retries with `Retry-After` handling and full-jitter backoff, a block/challenge circuit breaker, and conditional HTTP caching — composed by `AcquisitionPipelineFactory`. `Sanare.Http` supplies coherent, deterministic profile headers and host-scoped consent cookies through an explicit request-identity projection, detects supported consent walls and terminal CAPTCHA/login/paywall states, and accumulates credential-free compliance reports.

The Git repository stores versioned plans under a write lease with monotonic approval tags, and exposes plan history, diffs, heal branches, fast-forward promotion, rollback-by-name, diagnosis notes, and heal-branch pruning. The fixture corpus captures redacted, size-capped responses, deduplicates normalized content, rewrites its manifest atomically, applies DR-011 pyramid retention, and supports bounded slicing and offline replay without network access.

### Requirements

- .NET SDK 10.0 or later (`net10.0` target framework, `LangVersion` 14.0).

### Build

```powershell
dotnet build Sanare.slnx
```

### Test

```powershell
dotnet test Sanare.slnx
```

### Format check (CI-friendly)

```powershell
dotnet format Sanare.slnx --verify-no-changes
```

All three commands are expected to run clean (0 warnings/errors) against the current codebase; `TreatWarningsAsErrors` and `EnableNETAnalyzers` are enabled solution-wide via [Directory.Build.props](Directory.Build.props).

### What's intentionally unimplemented

The following are explicitly out of scope for v0.1 or remain incomplete after the governed acquisition pipeline:

- The browser-tier integration suite: a real-Chromium test site under `tests/Sanare.Browser.Tests/TestSite/` and the associated process-count/leak assertions (`docs/features/browser-tier.md` T14). The tier itself (gate, pool, page scope, `BrowserContentAcquirer`, `TieredContentAcquirer` dispatch, `PlaywrightChallengeHandoff`) is implemented and unit-tested, but never exercised against a live browser yet. JSON/structured-data (JSON-LD, microdata) extraction operations are also unimplemented.
- Plan authoring, LLM-assisted plan generation, and self-healing/repair workflows. The repository primitives those workflows need (heal branches, promotion, rollback, diagnosis notes) ship; the agent-driven workflow that drives them does not.
- Redirect policy and `llms.txt` discovery are implemented and tested, but neither is consulted yet by `GovernedContentAcquirer` or any runner.
- The git CLI backend for plan storage is descoped by decision rather than pending — DR-003 makes it an environment accommodation, not a functional requirement. Plan blame and merge/conflict resolution remain deferred (see [docs/features/script-repository.md](docs/features/script-repository.md) and [docs/features/plan-resolver.md](docs/features/plan-resolver.md)).
- Schema-aware validation at the repository commit boundary and plan authoring integration; commits already run structural validation, including version-2 compatibility, URL-template placeholder binding when request parameters are available, `MaxItems` bounds, and non-backtracking regex validation (see [docs/features/extraction-plan-model.md](docs/features/extraction-plan-model.md)).
- Pagination (`StreamAsync` throws `NotSupportedException` by design).
- End-to-end OpenTelemetry/Aspire wiring. The observability primitives (activity source, metrics, cardinality guard, redaction, audit log, alerting) exist and the acquisition pipeline emits through them, but neither runner is instrumented yet.

### Recommended next step

The controlled HTTP acquisition and browsing-identity boundary is live in the runtime path through `AcquisitionScrapeRunner`, and the **browser tier** (row 8 in the [feature index](docs/features/overview.md)) now composes and unit-tests its full dispatch path — `BrowserContentAcquirer`, `TieredContentAcquirer`, and `PlaywrightChallengeHandoff` — behind `AcquisitionPipelineFactory.CreateBrowser(...)`. The recommended next increment is the **browser-tier integration suite** (T14 in [docs/features/browser-tier.md](docs/features/browser-tier.md)): a real-Chromium test site under `tests/Sanare.Browser.Tests/TestSite/` and the process-count/leak assertions that only a live browser can exercise. That closes out Tier 3 plan execution and the `LoadMoreButton` / `InfiniteScroll` pagination modes; the authoring and healing workflows then build on the completed plan-runtime path. Instrumenting both runners against the existing observability primitives, and consulting the already-implemented redirect policy and `llms.txt` discovery from the governed acquirer, are smaller independent follow-ups.
