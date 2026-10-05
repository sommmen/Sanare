# Development

Work yet to be done in this repository. Keep this list current as work is
planned, started, and finished.

## Running locally

```powershell
dotnet build Sanare.slnx
dotnet test Sanare.slnx
dotnet format Sanare.slnx --verify-no-changes
```

Requires .NET SDK 10.0+. See [README.md](README.md) for the current architecture
and scope of the v0.1 foundation.

## Todo

- [x] Implement the v0.1 project skeleton, core domain/engine contracts, and a minimal deterministic offline engine path (`Sanare.Abstractions` + `Sanare.Core`), proving the intended architecture end-to-end via fixture-backed execution.
- [x] Implement the on-disk fixture corpus with mandatory redaction, normalized deduplication, atomic concurrent capture, DR-011 pyramid retention, bounded slicing, and zero-socket offline replay.
- [x] Implement the local Git-backed approved-plan storage and resolution slice (`script-repository`/`plan-resolver`) while preserving the in-memory provider for the minimal runner example.
- [x] Complete advanced extraction-plan repository operations: plan history, diffs, heal branches, fast-forward promotion, rollback-by-name, diagnosis notes, and heal-branch pruning. The optional git CLI backend was descoped — DR-003 makes it an environment accommodation rather than a functional requirement, and no acceptance criterion depends on it. Remote synchronization is out of scope for this component — the repository is local-only and a configured remote is the host's concern. Build order, preconditions, and deferred scope: [Implementation Plan](docs/features/script-repository.md#implementation-plan).
- [x] Implement `IPlanValidator`: structural and request-aware validation of an already-deserialized `ExtractionPlan` (field/schema coverage, operation arity/tier gating, non-backtracking regexes, pagination bounds, consent/URL/header/schema-hash checks, and optional URL-template placeholder binding).
- [x] Implement plan version-2 read compatibility (`SNR-PLAN-002` window handling), the unambiguous version-1 locator upgrade, and repository commit validation through `IPlanValidator`.
- [x] Implement a controlled HTTP acquisition foundation: GET-only HTTPS-by-default transport, bounded streamed reads, header/content-type/charset handling, fixture capture, and zero-socket offline replay. This boundary is not yet wired into `FixtureScrapeRunner`.
- [x] Implement browsing identity (`Sanare.Http`): coherent deterministic header composition, startup profile-coherence validation (`SNR-ID-001`/`SNR-ID-002`), per-host cookie jar, consent-wall detection with a single capped retry (`SNR-ACQ-005`), challenge/terminal-wall classification, and the per-source compliance report. Build order and deferred scope: [Implementation Plan](docs/features/browsing-identity.md#implementation-plan).
- [x] Implement the governed HTTP acquisition pipeline (`Sanare.Http`): the `AcquisitionOptions` policy model, per-host rate limiting and politeness delays, `robots.txt` parsing and enforcement, `llms.txt` discovery, retries with `Retry-After` handling and full-jitter backoff, the block/challenge circuit breaker, conditional HTTP caching, and redirect policy — composed by `GovernedContentAcquirer` and wired by `AcquisitionPipelineFactory`. Redirect policy and `llms.txt` discovery are implemented and tested but not yet consulted by the governed acquirer or any runner. Build order and deferred scope: [Implementation Plan](docs/features/acquisition-pipeline.md#implementation-plan).
- [x] Implement browser-tier (Playwright) acquisition (`Sanare.Browser`): the DR-004 double opt-in gate, the bounded context pool, context realism, the closed wait-strategy and interaction vocabularies, resource blocking, cookie hand-off, `BrowserContentAcquirer` composing all of it, a `TieredContentAcquirer` dispatching by `plan.Tier` with no fallback edge in either direction, and the DR-014 manual challenge hand-off (`PlaywrightChallengeHandoff`, the sole `IChallengeHandoff` implementation). Unit-level gating tests and tagged real-Chromium tests against a Kestrel-served deterministic test site cover rendered acquisition, interaction, resource blocking, pool exhaustion/idle eviction, and live challenge hand-off. Build order, preconditions, and deferred scope: [Implementation Plan](docs/features/browser-tier.md#implementation-plan).
- [ ] Implement the pagination engine and remove the `NotSupportedException` from `IScrapeRunner.StreamAsync`. Collection extraction (`IPlanExecutor.ExecuteMany`) now exists (single-page HTML and JSON-array roots, built for the Lenovo sample), but page-to-page enumeration (`LoadMoreButton`/`InfiniteScroll`/offset paging) is still unimplemented. Build order, preconditions, and deferred scope: [Implementation Plan](docs/features/pagination-engine.md#implementation-plan).
- [ ] Add formal test cases for the M2–M7 milestones, including fixture regressions and self-healing negative paths.
- [ ] Define the exact Microsoft Agent Framework and OmniRoute integration packages, configuration, and failure behavior before implementing plan authoring/healing.
- [x] Implement the offline slice of the Lenovo sample application (`samples/Sanare.Samples.Lenovo`): typed `TabletListing`/`TabletProduct`/`ProductSpecification` schemas, two hand-written `StructuredData` plans (Product `ld+json` + inline JSON island extraction, no Lenovo selectors in C#), a file-backed zero-socket fixture provider, and the `detail --offline`/`list --offline`/`validate` commands with golden-file integration tests and an architecture-guard test. The spec's `--live`, `capture`, and `approve` modes remain unimplemented pending the acquisition and authoring workflows. See [sample-app-lenovo.md](docs/features/sample-app-lenovo.md) and the sample's [README](samples/Sanare.Samples.Lenovo/README.md).
