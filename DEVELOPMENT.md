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
- [ ] Complete advanced extraction-plan repository operations: git CLI backend, heal branches, fast-forward promotion, rollback, garbage collection, and remote synchronization.
- [x] Implement `IPlanValidator`: structural and request-aware validation of an already-deserialized `ExtractionPlan` (field/schema coverage, operation arity/tier gating, non-backtracking regexes, pagination bounds, consent/URL/header/schema-hash checks, and optional URL-template placeholder binding).
- [x] Implement plan version-2 read compatibility (`SNR-PLAN-002` window handling), the unambiguous version-1 locator upgrade, and repository commit validation through `IPlanValidator`.
- [x] Implement a controlled HTTP acquisition foundation: GET-only HTTPS-by-default transport, bounded streamed reads, header/content-type/charset handling, fixture capture, and zero-socket offline replay. This boundary is not yet wired into `FixtureScrapeRunner`.
- [x] Implement browsing identity (`Sanare.Http`): coherent deterministic header composition, startup profile-coherence validation (`SNR-ID-001`/`SNR-ID-002`), per-host cookie jar, consent-wall detection with a single capped retry (`SNR-ACQ-005`), challenge/terminal-wall classification, and the per-source compliance report. Build order and deferred scope: [Implementation Plan](docs/features/browsing-identity.md#implementation-plan).
- [ ] Complete the rest of the governed acquisition pipeline and browser-tier (Playwright) acquisition per `docs/sanare/tech-design.md`: runner integration, audited Stealth capability implementations, robots/discovery, per-host pacing, retries/circuit breaking, cache and redirect policy, telemetry, and browser escalation.
- [ ] Implement the pagination engine and remove the `NotSupportedException` from `IScrapeRunner.StreamAsync`. This also requires collection extraction (`IPlanExecutor.ExecuteMany`), which does not exist yet. Build order, preconditions, and deferred scope: [Implementation Plan](docs/features/pagination-engine.md#implementation-plan).
- [ ] Add formal test cases for the M2–M7 milestones, including fixture regressions and self-healing negative paths.
- [ ] Define the exact Microsoft Agent Framework and OmniRoute integration packages, configuration, and failure behavior before implementing plan authoring/healing.
