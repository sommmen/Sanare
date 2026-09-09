# Sanare

Sanare is a draft-first .NET library for schema-driven, self-healing product-data extraction. It uses the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) to author, version, execute, evaluate, and repair extraction plans while preferring efficient network and structured-data acquisition before escalating to Playwright.

## Documentation

- [Technical design](docs/sanare/tech-design.md) — canonical architecture, operational decisions, and delivery milestones.
- [Feature specifications](docs/features/overview.md) — ordered implementation index and component specifications.
- [Product idea](ideas/sanare/draft.md) — goals, reference scenarios, and MVP boundaries.

## Scope

The initial reference scenarios cover Lenovo tablet-list pagination and product/spec-table extraction. The design supports typed schema outputs, offline fixture-based validation, Git-backed plan history, source-level LLM budgets, adaptive request pacing and caching, and OpenTelemetry/Aspire-compatible observability.

## Current status: v0.1 foundation

This repository currently implements a **v0.1 foundation**: the project skeleton, the core domain/engine contracts, a minimal deterministic offline execution path, a local Git-backed approved-plan storage slice, the on-disk fixture corpus, and a partial controlled HTTP acquisition boundary. The existing `FixtureScrapeRunner` remains fixture-backed; the independent acquirer is not yet wired into that execution path. Plan authoring/healing, browser automation, pagination, and the remainder of the designed acquisition pipeline remain unimplemented — see [What's intentionally unimplemented](#whats-intentionally-unimplemented).

### Project structure

| Project | Purpose |
| --- | --- |
| `src/Sanare.Abstractions` | Core domain and engine contracts: `ScrapeRequest`/`ScrapeResult`/`ScrapeStatus`, the `IScrapeRunner` execution interface, extraction-plan models, the closed `PlanOperation` vocabulary, schema attributes, diagnostics, and quality/provenance types. No infrastructure dependencies. |
| `src/Sanare.Core` | The v0.1 engine and local infrastructure: schema derivation/coercion/materialization, an AngleSharp-backed HTML document adapter, a narrow `PlanExecutor`, `FixtureScrapeRunner`, local Git-backed approved-plan storage, and an on-disk fixture corpus with mandatory redaction and DR-011 retention. |
| `tests/Sanare.Abstractions.Tests` | Contract tests for request validation and the operation catalog. |
| `tests/Sanare.Core.Tests` | Unit and integration-style tests for schema execution, fixture capture/redaction/normalization/retention, Git-backed plan storage, and deterministic zero-socket offline replay. |

### Engine execution path (v0.1)

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

The minimal runner example remains usable with hand-authored plans through `InMemoryExtractionPlanProvider` and deterministic content through `InMemoryFixtureContentProvider`. The repository also includes a partial local Git-backed approved-plan repository/resolver, an independent on-disk fixture corpus, and a partial `HttpContentAcquirer` foundation. The acquirer supports GET-only HTTPS requests by default, streamed 16 MiB response limits, response-header normalization, content-type validation, charset detection, fixture capture, and zero-socket fixture replay. The corpus captures redacted, size-capped responses, deduplicates normalized content, rewrites its manifest atomically, applies DR-011 pyramid retention, and supports bounded slicing and offline replay without network access.

### Requirements

- .NET SDK 9.0 or later (`net9.0` target framework, `LangVersion` 13.0).

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

The following are explicitly out of scope for v0.1 or remain incomplete after the controlled HTTP acquisition foundation:

- Integration of `HttpContentAcquirer` into `FixtureScrapeRunner` or the planned runtime path.
- HTTP acquisition policy and resilience: robots.txt/llms.txt, host pacing and concurrency, retries, circuit breaking and challenge hand-off, cache/conditional requests, redirect-hop limits, cookie handling, browsing identity, and acquisition diagnostics/telemetry.
- Browser acquisition (Playwright) and structured-data acquisition.
- Plan authoring, LLM-assisted plan generation, and self-healing/repair workflows.
- The unimplemented remainder of Git-backed plan storage/resolution and `IPlanValidator` structural validation — advanced history/diff, heal branches, rollback, CLI-backed Git operation, and authoring integration remain deferred (see [docs/features/script-repository.md](docs/features/script-repository.md), [docs/features/plan-resolver.md](docs/features/plan-resolver.md), and [docs/features/extraction-plan-model.md](docs/features/extraction-plan-model.md)).
- Pagination (`StreamAsync` throws `NotSupportedException` by design).
- Browser-tier acquisition (Playwright) and JSON/structured-data (JSON-LD, microdata) extraction operations.
- Observability/telemetry (OpenTelemetry/Aspire), caching, and request pacing.

### Recommended next step

The recommended next architecture increment is to complete the **persistent extraction-plan model and validation boundary**: add `IPlanValidator`, canonical/versioned plan serialization, and the remaining `plan-resolver`/`script-repository` operations before introducing LLM authoring and healing workflows.
