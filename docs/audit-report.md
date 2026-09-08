# Documentation Audit Report — Persistent Extraction-Plan Storage Slice

**Date**: 2025-06 (session audit, post-implementation of the Git-backed plan
storage/resolution slice)
**Scope**: `docs/features/script-repository.md`, `docs/features/plan-resolver.md`,
`docs/features/extraction-plan-model.md`, `docs/features/overview.md`,
`docs/sanare/tech-design.md`, cross-referenced against
`src/Sanare.Core/Repository/`, `src/Sanare.Core/Resolution/`,
`src/Sanare.Core/Plans/`, `src/Sanare.Core/GitBackedExtractionPlanProvider.cs`,
and their test suites under `tests/Sanare.Core.Tests/`.
**Method**: Code-grounded cross-reference. Every finding below cites the
specific file/line evidence found during this pass; no issues are inferred
without a matching code or doc citation.

## Executive Summary

The newly implemented persistent/Git-backed extraction-plan storage slice is
well aligned with its governing specs. `docs/features/script-repository.md`
and `docs/features/plan-resolver.md` accurately describe the implemented
"Implemented slice" scope, correctly separate it from "Deferred target-state
scope," and their error-handling/acceptance-criteria tables now match the
thrown `SNR-*` codes in code (two gaps found and fixed during this audit — see
Major findings, both resolved). `docs/sanare/tech-design.md`'s M3 milestone row
carries an accurate "Implementation note" scoping caveat, and
`docs/features/overview.md` correctly marks both specs `partial`.

Five stale/incorrect cross-reference issues were found in source XML doc
comments (heading links that no longer matched the current spec headings, and
one factually wrong claim about jitter in the retry coordinator) — all five
were corrected during this audit. Two genuine test-coverage gaps remain open
and are **not** silently fixed here, per audit scope (documentation vs. code
alignment, not new test authoring): `GitBackedExtractionPlanProvider` has zero
direct unit tests, and the `SNR-GIT-006` error path has zero test coverage.
Both are flagged as Major findings with concrete remediation recommendations.

## Findings Summary

| Severity | Count | Categories |
|----------|-------|------------|
| Critical | 0 | — |
| Major    | 2 | Test coverage gaps (adapter, error path) |
| Minor    | 0 | — |
| Info     | 5 | Stale cross-references (fixed during audit) |

## Major Findings

### MAJ-001: `GitBackedExtractionPlanProvider` has no direct unit tests
- **Location**: [GitBackedExtractionPlanProvider.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/GitBackedExtractionPlanProvider.cs); documented as the runner-facing adapter in [plan-resolver.md](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/docs/features/plan-resolver.md) (Core Responsibilities, Interfaces/Dependencies, and Test Module sections).
- **Issue**: The adapter that bridges `IPlanResolver.ResolveAsync` to the runner-facing synchronous `IExtractionPlanProvider.TryGet` — including its blocking `.AsTask().GetAwaiter().GetResult()` call and its translation of `PlanResolution.IsResolved`/failure into the `bool TryGet(...)` contract — has no test file exercising it directly. `tests/Sanare.Core.Tests/` contains `Resolution/PlanResolverTests.cs` and `Repository/GitScriptRepositoryTests.cs`, but no `GitBackedExtractionPlanProviderTests.cs` (or equivalent).
- **Evidence**: `Glob`/directory search of `tests/Sanare.Core.Tests/` confirms no test file references `GitBackedExtractionPlanProvider`. The class itself is small but contains real logic (sync-over-async bridging, out-parameter population, and mapping `PlanResolutionFailure`/exceptions to a `false` return) that is exactly the kind of adapter logic unit tests exist to protect.
- **Impact**: A regression in the sync/async bridging (e.g., a hang under a synchronization context, or a mismapped failure case returning `true` with a default plan) would not be caught by the existing resolver/repository test suites, since those test the components the adapter wraps, not the adapter's own translation logic.
- **Fix**: Add `tests/Sanare.Core.Tests/GitBackedExtractionPlanProviderTests.cs` covering: (1) `TryGet` returns `true` and populates `plan` on a resolved plan; (2) `TryGet` returns `false` on `NoPlanAvailable`/`SchemaDrift`/`PlanInvalid`; (3) the call does not deadlock under a single-threaded `SynchronizationContext` (regression guard for the blocking `GetAwaiter().GetResult()` pattern). Left unimplemented in this audit pass — this is a recommendation, not applied.

### MAJ-002: `SNR-GIT-006` (`VersionAlreadyApproved`) has no test coverage
- **Location**: [GitScriptRepository.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/GitScriptRepository.cs) `ApproveInternalAsync` (the `SNR-GIT-006` throw site); documented in [script-repository.md](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/docs/features/script-repository.md) Error Handling table and `AC-GIT-008`.
- **Issue**: The path where the computed next monotonic approval tag name (`prefix + nextNumber`) already exists in the repository but does **not** point at the commit being approved (i.e., a real conflict, as opposed to the idempotent same-commit short-circuit a few lines above) throws `ScriptRepositoryException("SNR-GIT-006", ...)`. No test in `tests/Sanare.Core.Tests/Repository/GitScriptRepositoryTests.cs` exercises this branch.
- **Evidence**: Search of `GitScriptRepositoryTests.cs` for `SNR-GIT-006` and `VersionAlreadyApproved` returns no matches. The only approval-related tests found exercise the idempotent-same-commit path (`AC-GIT-008`) and the happy-path monotonic tag creation, not the conflicting-tag case.
- **Impact**: This is a narrow race/pathological condition (an external process or a concurrent `ApproveAsync` call creating a same-named tag pointing at a different commit between the read and the write), so its likelihood is low, but it is also the only code path that currently has zero coverage of any kind for a documented, user-visible error code (`SNR-GIT-006` is in the Error Handling table and referenced in `tech-design.md`). The doc table implies a tested contract even though nothing currently verifies the throw actually fires with the right code/message.
- **Fix**: Add a unit test to `GitScriptRepositoryTests.cs` that pre-creates a tag at the computed next tag name pointing at a different commit than the one being approved, then asserts `ApproveAsync` throws `ScriptRepositoryException` with code `SNR-GIT-006`. Left unimplemented in this audit pass — this is a recommendation, not applied, since writing new tests is outside a documentation/code alignment audit's scope unless requested.

## Observations & Suggestions (fixed during this audit)

The following five issues were found and corrected in place, since they were
factual/reference errors rather than open design questions:

### INFO-001: Stale `RepositoryOptions.ICoordinatorFactory` reference [RESOLVED]
- **Location**: `docs/features/script-repository.md` (~former line 174–177).
- **Issue**: The spec described a `RepositoryOptions.ICoordinatorFactory` extension point that does not exist anywhere in the codebase (no DI container or factory abstraction exists in this slice; `GitScriptRepository` and `IRepositoryCoordinator` implementations are constructed manually).
- **Fix applied**: Replaced with accurate wording describing constructor-injected `IRepositoryCoordinator`, with `FileLockRepositoryCoordinator` as the only current implementation.

### INFO-002: Incorrect "retrying with jitter" claim [RESOLVED]
- **Location**: [IRepositoryCoordinator.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/IRepositoryCoordinator.cs).
- **Issue**: XML doc comment claimed the coordinator retries "with jitter," but `FileLockRepositoryCoordinator` uses a fixed `RetryDelay = TimeSpan.FromMilliseconds(50)` — no jitter/randomization is implemented.
- **Fix applied**: Corrected to "retrying at a fixed delay"; also fixed a stale spec-heading link ("Repository coordination (DR-013)" → "Repository coordination and integrity", the actual current heading).

### INFO-003: Stale spec-heading cross-links in `GitRef.cs`, `PlanCommitInfo.cs`, `PlanCommitRequest.cs`, `FileLockRepositoryCoordinator.cs` [RESOLVED]
- **Location**: [GitRef.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/GitRef.cs), [PlanCommitInfo.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/PlanCommitInfo.cs), [PlanCommitRequest.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/PlanCommitRequest.cs), [FileLockRepositoryCoordinator.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/FileLockRepositoryCoordinator.cs).
- **Issue**: Each referenced a `docs/features/script-repository.md` heading in parentheses (e.g., "Commit message format", "Concurrency and integrity") that no longer exists verbatim after the spec was rewritten during this slice's documentation update. Current headings enumerated via `Select-String -Pattern '^#|^##|^###'` are: Purpose, Scope, Core Responsibilities, Interfaces, Data Flow, Key Behaviors (Interface / Paths and naming / Commit and bootstrap behavior / Repository coordination and integrity), Constraints, Acceptance Criteria, Error Handling, File Structure, Test Module.
- **Fix applied**: Updated each reference to point at the actual current heading ("Commit and bootstrap behavior" or "Repository coordination and integrity" as appropriate).

### INFO-004: Stale "The index" heading references in `IScriptRepository.cs` and `ApprovalTagParser.cs` [RESOLVED]
- **Location**: [IScriptRepository.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Repository/IScriptRepository.cs), [ApprovalTagParser.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Resolution/ApprovalTagParser.cs).
- **Issue**: Both referenced `docs/features/plan-resolver.md ("The index")`, a heading that does not exist verbatim; the current spec's equivalent heading is "Warm index and tag resolution" (under Key Behaviors).
- **Fix applied**: Updated both references to "Warm index and tag resolution".

### INFO-005: Missing `SNR-PLAN-001` and `SNR-GIT-006` spec table rows [RESOLVED]
- **Location**: `docs/features/plan-resolver.md` Error Handling / Acceptance Criteria tables; `docs/features/script-repository.md` Error Handling / Acceptance Criteria tables.
- **Issue**: `SNR-PLAN-001` (`PlanInvalid`, thrown by [PlanResolver.cs](/C:/repos/Sanare.worktrees/featurepersistent-extraction-plan-storage/src/Sanare.Core/Resolution/PlanResolver.cs) on canonical deserialization failure, and tested by `PlanResolverTests.ResolveAsync_returns_PlanInvalid_when_the_stored_document_is_malformed`) was implemented and tested but absent from `plan-resolver.md`'s tables. `SNR-GIT-006` (see MAJ-002 above) was implemented and referenced in `tech-design.md` but absent from `script-repository.md`'s tables; `IScriptRepository.cs`'s XML doc already referenced an `AC-GIT-008` that did not yet exist in the spec.
- **Fix applied**: Added an `SNR-PLAN-001` row to `plan-resolver.md`'s Error Handling table and an `AC-PR-005` acceptance-criteria row (malformed stored document → `PlanInvalid`, not cached). Added an `SNR-GIT-006` row to `script-repository.md`'s Error Handling table and an `AC-GIT-008` acceptance-criteria row (idempotent approval of the same commit). Confirmed the added `SNR-GIT-006` wording ("The computed next monotonic tag name already exists (concurrent/external tag creation)") is exactly consistent with the trigger condition in `GitScriptRepository.ApproveInternalAsync` (the tag-exists check at the line that throws `SNR-GIT-006`, distinct from the same-commit idempotency short-circuit a few lines above it).

## Confirmed Non-Issues (checked, no gap found)

- **No DI container in this slice**: Verified via search — no `IServiceCollection`/`AddScoped`/`AddSingleton` usage anywhere in the repository. `GitScriptRepository` and `GitBackedExtractionPlanProvider` are constructed manually by callers. This is consistent with the foundation-only scope of this slice (`hosting-configuration.md` is milestone M7, not yet in scope) and is not a documentation gap.
- **Runner contract preserved**: `IExtractionPlanProvider.TryGet(string sourceId, Type schemaType, out ExtractionPlan plan)` is byte-for-byte unchanged from before this slice; `GitBackedExtractionPlanProvider` is a pure adapter. Confirmed via direct symbol read.
- **`docs/features/overview.md`** correctly marks both `script-repository` and `plan-resolver` rows as `partial` status, consistent with the "Implemented slice" vs. "Deferred target-state scope" split in each spec.
- **`docs/sanare/tech-design.md`** M3 milestone row carries an accurate "Implementation note" (line ~1853) correctly scoping what is and is not done (bootstrap/commits/tags/warm-cold resolution done; admin API/rollback/diff deferred).
- **`extraction-plan-model.md`** headings ("Canonical serialization", "Error Handling") referenced from `IPlanSerializer.cs`, `PlanSerializer.cs`, and `PlanSerializationException.cs` were verified against the file's actual heading list and are all accurate — no fix needed.
- **`ResolvedPlan.cs`, `PlanResolution.cs`, `PlanResolutionFailure.cs`, `PlanResolutionRequest.cs`, `PlanDocument.cs`** heading references ("Outputs", "Inputs", "Interface") were checked against each spec's actual `### Inputs` / `### Outputs` / Key Behaviors subheadings and are accurate.

## Recommended Priority Actions

1. **Add `GitBackedExtractionPlanProviderTests.cs`** — fixes MAJ-001 — effort: small (adapter is a thin translation layer; 3–4 focused unit tests suffice).
2. **Add an `SNR-GIT-006` conflict-path unit test to `GitScriptRepositoryTests.cs`** — fixes MAJ-002 — effort: small (requires pre-seeding a conflicting tag in the test fixture's temp repository).

Both recommendations are deliberately left unimplemented in this audit pass — the task scope was a documentation/code cross-check, not new test authoring. Implement on request.

## Validation Performed

- `dotnet build` — succeeded, 0 warnings, 0 errors (after the XML-doc-only source edits listed above).
- `dotnet test` — 52/52 passed (`Sanare.Abstractions.Tests`: 11, `Sanare.Core.Tests`: 41). No test changes were made; existing coverage is unaffected by this audit's edits.
- `dotnet format --verify-no-changes` — clean, no formatting drift.
- `git diff --check` — no whitespace errors (only expected CRLF→LF line-ending notices on doc files).
