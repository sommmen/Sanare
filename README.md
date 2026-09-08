# Sanare

Sanare is a draft-first .NET library for schema-driven, self-healing product-data extraction. It uses the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) to author, version, execute, evaluate, and repair extraction plans while preferring efficient network and structured-data acquisition before escalating to Playwright.

## Documentation

- [Technical design](docs/sanare/tech-design.md) — canonical architecture, operational decisions, and delivery milestones.
- [Feature specifications](docs/features/overview.md) — ordered implementation index and component specifications.
- [Product idea](ideas/sanare/draft.md) — goals, reference scenarios, and MVP boundaries.

## Scope

The initial reference scenarios cover Lenovo tablet-list pagination and product/spec-table extraction. The design supports typed schema outputs, offline fixture-based validation, Git-backed plan history, source-level LLM budgets, adaptive request pacing and caching, and OpenTelemetry/Aspire-compatible observability.

## Current status: v0.1 foundation

This repository currently implements a **v0.1 foundation**: the project skeleton, the core domain/engine contracts, and a minimal, deterministic, offline execution path that proves the intended architecture end-to-end. No user-facing product features (live acquisition, plan authoring, browser automation, pagination, persistence) are implemented yet — see [What's intentionally unimplemented](#whats-intentionally-unimplemented).

### Project structure

| Project | Purpose |
| --- | --- |
| `src/Sanare.Abstractions` | Core domain and engine contracts: `ScrapeRequest`/`ScrapeResult`/`ScrapeStatus`, the `IScrapeRunner` execution interface, extraction-plan models, the closed `PlanOperation` vocabulary, schema attributes, diagnostics, and quality/provenance types. No infrastructure dependencies. |
| `src/Sanare.Core` | The minimal v0.1 engine: schema derivation/coercion/materialization, an AngleSharp-backed HTML document adapter, a narrow `PlanExecutor`, in-memory plan/fixture provider boundaries, and `FixtureScrapeRunner`, the `IScrapeRunner` implementation that composes the full path. |
| `tests/Sanare.Abstractions.Tests` | Contract tests for request validation and the operation catalog. |
| `tests/Sanare.Core.Tests` | Unit tests for schema derivation/coercion and an integration-style test suite exercising `FixtureScrapeRunner` end-to-end (success, invalid input, and each error path). |

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

Plans are hand-authored in-process via `InMemoryExtractionPlanProvider` (no untrusted JSON is deserialized in this milestone), and fixture content is served from `InMemoryFixtureContentProvider`. Both are explicit infrastructure boundaries designed to be replaced by persistent/live implementations without changing `Sanare.Core`'s core logic.

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

The following are explicitly out of scope for v0.1 and are left as extension points for future feature work, not partially-built infrastructure:

- Live HTTP/browser acquisition (only deterministic fixture content is served).
- Plan authoring, LLM-assisted plan generation, and self-healing/repair workflows.
- Persistent/Git-backed plan storage and `IPlanValidator` structural validation — both belong to the full `extraction-plan-model` feature (see [docs/features/extraction-plan-model.md](docs/features/extraction-plan-model.md)) and are deferred because v0.1 plans are hand-authored in-process, not deserialized from untrusted JSON.
- Pagination (`StreamAsync` throws `NotSupportedException` by design).
- Browser-tier acquisition (Playwright) and JSON/structured-data (JSON-LD, microdata) extraction operations.
- A committed fixture corpus (fixtures currently live inline in test code).
- Observability/telemetry (OpenTelemetry/Aspire), caching, and request pacing.

### Recommended next step

The recommended first architecture stress test is **persistent extraction-plan storage and resolution** (the `plan-resolver`/`script-repository` features): swapping `InMemoryExtractionPlanProvider` for a Git-backed or file-backed plan store is the smallest change that exercises a new infrastructure boundary without requiring the LLM authoring/healing workflows, and it unblocks introducing `IPlanValidator` for real, since plans would then originate from outside the process.
