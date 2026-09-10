# Documentation Audit Report — Sanare

**Latest full-repository pass**: 2026-09-10 (documentation/code alignment)

## Latest Full-Repository Audit Pass

**Scope**: All project documentation under `docs/`, root project guidance, and
active implementation-status material under `ideas/sanare/`, cross-referenced
against the current source and test trees. The earlier persistent extraction-plan
storage-slice audit is retained below as resolved historical evidence.
**Method**: Code-grounded cross-reference. Code alignment was included using the
recommended default when the user was unavailable for the workflow's optional
confirmation. Historical idea/research/session notes were treated as provenance,
not as current implementation commitments.

**Open findings from this pass**: 1 Major and 2 Minor. MAJ-003, MIN-002, and NEW-006
are the open findings from this pass. MAJ-003 and MIN-002 belong to other
component scopes and are report-only for their owners. MIN-001 (script-repository),
MIN-003, MIN-004, MIN-005, and NEW-007 from the 2026-09-06 pass are now resolved
(see the 2026-09-10 section below); one new Minor finding (NEW-006) remains open
for a schema-engine materialization-method deviation. MIN-001's resolution added
a real unit test for the previously untested `SNR-GIT-004` lease-timeout behavior
and corrected the script-repository test-module inventory. NEW-007's resolution
split `plan-runtime.md` into implemented vs. target-state sections to clarify the
v0.1 scope. All other fixes in this pass remain documentation-only.

## Persistent Extraction-Plan Storage Slice Audit (Historical)

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
were corrected during this audit. Two genuine test-coverage gaps were found —
`GitBackedExtractionPlanProvider` had zero direct unit tests, and the
`SNR-GIT-006` error path had zero test coverage — and both are now **resolved**:
direct unit tests were added for `GitBackedExtractionPlanProvider`'s full public
surface, and a deterministic `SNR-GIT-006` conflict-path test was added to
`GitScriptRepositoryTests.cs`. See MAJ-001 and MAJ-002 below for the applied fix
and verification evidence.

## Findings Summary

| Severity | Count | Categories |
|----------|-------|------------|
| Critical | 0 | — |
| Major    | 2 | Test coverage gaps (adapter, error path) — both resolved |
| Minor    | 0 | — |
| Info     | 5 | Stale cross-references (fixed during audit) |

## Major Findings

### MAJ-001: `GitBackedExtractionPlanProvider` has no direct unit tests — **RESOLVED**
- **Location**: [GitBackedExtractionPlanProvider.cs](../../src/Sanare.Core/GitBackedExtractionPlanProvider.cs); documented as the runner-facing adapter in [plan-resolver.md](features/plan-resolver.md) (Core Responsibilities, Interfaces/Dependencies, and Test Module sections).
- **Issue**: The adapter that bridges `IPlanResolver.ResolveAsync` to the runner-facing synchronous `IExtractionPlanProvider.TryGet` — including its blocking `.AsTask().GetAwaiter().GetResult()` call and its translation of `PlanResolution.IsResolved`/failure into the `bool TryGet(...)` contract — has no test file exercising it directly. `tests/Sanare.Core.Tests/` contains `Resolution/PlanResolverTests.cs` and `Repository/GitScriptRepositoryTests.cs`, but no `GitBackedExtractionPlanProviderTests.cs` (or equivalent).
- **Evidence**: `Glob`/directory search of `tests/Sanare.Core.Tests/` confirms no test file references `GitBackedExtractionPlanProvider`. The class itself is small but contains real logic (sync-over-async bridging, out-parameter population, and mapping `PlanResolutionFailure`/exceptions to a `false` return) that is exactly the kind of adapter logic unit tests exist to protect.
- **Impact**: A regression in the sync/async bridging (e.g., a hang under a synchronization context, or a mismapped failure case returning `true` with a default plan) would not be caught by the existing resolver/repository test suites, since those test the components the adapter wraps, not the adapter's own translation logic.
- **Fix applied**: Added [GitBackedExtractionPlanProviderTests.cs](../../tests/Sanare.Core.Tests/GitBackedExtractionPlanProviderTests.cs), covering the adapter's full public surface end to end against a real temporary Git repository plus `PlanResolver` (`TryGet_returns_resolved_plan_from_the_latest_approval_tag`), the documented resolution-failure translations for `NoPlanAvailable`/`SchemaDrift`/`PlanInvalid` (`TryGet_returns_false_when_resolution_fails`, a `[Theory]` over all three `PlanResolutionFailure` values), the request built from derived schema metadata (`TryGet_passes_derived_schema_metadata_to_the_resolver`), null-argument validation (`TryGet_throws_for_null_arguments`), and resolver-exception propagation (`TryGet_propagates_resolver_exceptions`). All 7 tests pass; see Validation Performed below.

### MAJ-002: `SNR-GIT-006` (`VersionAlreadyApproved`) has no test coverage — **RESOLVED**
- **Location**: [GitScriptRepository.cs](../../src/Sanare.Core/Repository/GitScriptRepository.cs) `ApproveInternalAsync` (the `SNR-GIT-006` throw site); documented in [script-repository.md](features/script-repository.md) Error Handling table and `AC-GIT-008`.
- **Issue**: The path where the computed next monotonic approval tag name (`prefix + nextNumber`) already exists in the repository but does **not** point at the commit being approved (i.e., a real conflict, as opposed to the idempotent same-commit short-circuit a few lines above) throws `ScriptRepositoryException("SNR-GIT-006", ...)`. No test in `tests/Sanare.Core.Tests/Repository/GitScriptRepositoryTests.cs` exercises this branch.
- **Evidence**: Search of `GitScriptRepositoryTests.cs` for `SNR-GIT-006` and `VersionAlreadyApproved` returns no matches. The only approval-related tests found exercise the idempotent-same-commit path (`AC-GIT-008`) and the happy-path monotonic tag creation, not the conflicting-tag case.
- **Impact**: This is a narrow race/pathological condition (an external process or a concurrent `ApproveAsync` call creating a same-named tag pointing at a different commit between the read and the write), so its likelihood is low, but it is also the only code path that currently has zero coverage of any kind for a documented, user-visible error code (`SNR-GIT-006` is in the Error Handling table and referenced in `tech-design.md`). The doc table implies a tested contract even though nothing currently verifies the throw actually fires with the right code/message.
- **Fix applied**: Testing this branch deterministically requires simulating a concurrent tag creation between the tag-name computation and the `repo.Tags[tagName] is not null` collision check in `ApproveInternalAsync`, since pre-seeding tags before calling `ApproveAsync` is simply absorbed into the monotonic-numbering scan and never reaches the conflict branch. A minimal, behavior-neutral test seam was added to [GitScriptRepository.cs](../../src/Sanare.Core/Repository/GitScriptRepository.cs): an `internal static` `AsyncLocal`-backed hook, `ApprovalConflictSimulation`, invoked immediately after `tagName` is computed and immediately before the collision check, plus `[assembly: InternalsVisibleTo("Sanare.Core.Tests")]` so only the test assembly can observe it. The hook is `null` (a no-op) for every non-test caller, so production behavior is unchanged. The new test `ApproveAsync_throws_SNR_GIT_006_when_the_computed_next_tag_is_created_concurrently` in [GitScriptRepositoryTests.cs](../../tests/Sanare.Core.Tests/Repository/GitScriptRepositoryTests.cs) uses the hook to insert the exact computed conflicting tag at the TOCTOU boundary, then asserts `ApproveAsync` throws `ScriptRepositoryException` with `Code == "SNR-GIT-006"`, `Retryable == false`, and the exact contract message, and that the externally-inserted tag is left untouched (the approval was refused, not silently overwritten). The test was proven meaningful by temporarily neutering the `SNR-GIT-006` throw condition (`if (repo.Tags[tagName] is not null)` → `if (false && ...)`) and confirming the test then fails with `LibGit2Sharp.NameConflictException` instead of the expected exception, before reverting the neutering.

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

1. ~~**Add `GitBackedExtractionPlanProviderTests.cs`** — fixes MAJ-001 — effort: small (adapter is a thin translation layer; 3–4 focused unit tests suffice).~~ **Done** — see MAJ-001.
2. ~~**Add an `SNR-GIT-006` conflict-path unit test to `GitScriptRepositoryTests.cs`** — fixes MAJ-002 — effort: small (requires pre-seeding a conflicting tag in the test fixture's temp repository).~~ **Done** — see MAJ-002 (required a minimal test seam rather than pre-seeding, since pre-seeded tags are absorbed by the monotonic-numbering scan; see MAJ-002 Fix applied for detail).

Both recommendations were implemented in a follow-up test-coverage pass (see MAJ-001 and MAJ-002 "Fix applied" above); the original audit pass above them was scoped to documentation/code cross-checking only.

## Validation Performed

- `dotnet build` — succeeded, 0 warnings, 0 errors (after the XML-doc-only source edits listed above, and again after the MAJ-001/MAJ-002 test-coverage fixes below).
- `dotnet test` — 52/52 passed at the time of the original documentation/code cross-check (`Sanare.Abstractions.Tests`: 11, `Sanare.Core.Tests`: 41); no test changes were made in that pass. After the follow-up MAJ-001/MAJ-002 test-coverage pass: 66/66 passed (`Sanare.Abstractions.Tests`: 11, `Sanare.Core.Tests`: 55 — the original 41 plus 7 new `GitBackedExtractionPlanProviderTests` and 1 new `SNR-GIT-006` test in `GitScriptRepositoryTests.cs`; no existing test was weakened, skipped, or removed). The `SNR-GIT-006` test was confirmed non-vacuous by temporarily neutering the throw condition and observing the test fail with a different exception type, then reverting.
- `dotnet format --verify-no-changes` — clean, no formatting drift (verified again after the test-coverage pass).
- `git diff --check` — no whitespace errors (only expected CRLF→LF line-ending notices on doc files).

## Full-Repository Findings — 2026-09-06

### Findings Summary

| Severity | Open | Resolved historical | Category |
|----------|-----:|-------------------:|----------|
| Critical | 0 | 0 | — |
| Major | 1 | 2 | Implemented-scope mismatch |
| Minor | 5 | 0 | Status and test-inventory staleness |
| Info | 0 | 5 | — |

> **Update (2026-09-10)**: MIN-001, MIN-003, MIN-004, and MIN-005 are now resolved — see
> [Full-Repository Findings — 2026-09-10](#full-repository-findings--2026-09-10)
> below for evidence and the updated summary table.

### Major Findings

#### MAJ-003: Extraction-plan model spec presents deferred validation as implemented

- **Location**: `docs/features/extraction-plan-model.md` — Purpose, Scope,
  `IPlanValidator` interface, proposed layout, and test-module inventory.
- **Issue**: The specification describes structural validation as included and
  presents `IPlanValidator`, `PlanValidator`, and `PlanValidatorTests.cs` as
  implementation artifacts. No such production types or test module exist.
  `src/Sanare.Core/Plans/PlanSerializer.cs` explicitly identifies full
  structural validation as deferred to the `extraction-plan-model` scope.
- **Impact**: Readers can reasonably conclude that the safety boundary and its
  acceptance coverage are available when the current implementation supplies
  serialization only.
- **Recommended fix**: The `extraction-plan-model` owner should either implement
  the validator and its tests or revise the spec's implemented-slice language to
  distinguish the current serializer-only foundation from the target state.
- **Ownership**: `src/Sanare.Core/Plans/**` and `src/Sanare.Abstractions/Plans/**`
  are owned by the extraction-plan-model component; this is report-only.

### Minor Findings

#### MIN-001: Script-repository test inventory names a nonexistent standalone coordinator suite — ✅ RESOLVED 2026-09-10

- **Location**: `docs/features/script-repository.md` — test-module inventory.
- **Issue**: The specification named `FileLockRepositoryCoordinatorTests.cs`,
  but that file was absent. Additionally, no existing test actually exercised
  `FileLockRepositoryCoordinator`'s exclusive-acquisition/retryable-timeout
  behavior (`SNR-GIT-004`, AC-GIT-007) — the doc's claim was also a coverage
  gap, not just a naming mismatch.
- **Impact**: The documentation overstated the structure of focused coverage,
  and the lease-timeout acceptance criterion had no test backing it.
- **Recommended fix**: Correct the inventory to name the actual test coverage,
  or add the stated standalone test suite if that granularity is intended.
- **Ownership**: `src/Sanare.Core/Repository/**` is owned by the
  script-repository component; this session is that owner's active work.
- **Resolution**: Added
  `GitScriptRepositoryTests.CommitPlanAsync_throws_retryable_SNR_GIT_004_when_the_write_lease_times_out`,
  which pre-holds `.sanare-lock` exclusively with a short `LockTimeout` and
  asserts the coordinator's retry loop raises retryable `SNR-GIT-004`,
  closing the AC-GIT-007 coverage gap. `docs/features/script-repository.md`'s
  test-module inventory now describes this coverage inside
  `GitScriptRepositoryTests.cs` and no longer claims a standalone
  `FileLockRepositoryCoordinatorTests.cs` file exists.

#### MIN-002: Scrape API contracts test inventory lists test modules that are absent

- **Location**: `docs/features/scrape-api-contracts.md` — test-module inventory.
- **Issue**: `PublicApiApprovalTests.cs`, `ScrapeStatusCodesTests.cs`, and
  `DiagnosticSanitizerTests.cs` are listed but absent; only
  `RequestValidatorTests.cs` exists in the corresponding test area.
- **Impact**: The spec misrepresents the available contract coverage.
- **Recommended fix**: The scrape-api-contracts owner should implement the
  listed modules or update the inventory to reflect actual coverage.
- **Ownership**: `src/Sanare.Abstractions/**` is frozen/out of scope for this
  session; this is report-only for the scrape-api-contracts owner.

#### MIN-003: Schema-engine test inventory lists test modules that are absent — ✅ RESOLVED 2026-09-10

- **Location**: `docs/features/schema-engine.md` — test-module inventory.
- **Issue**: `SchemaHasherTests.cs`, `SchemaValidatorTests.cs`, and
  `QualityReportBuilderTests.cs` are listed but absent. The existing suite
  includes `TypeCoercerTests.cs` and `SchemaDeriverTests.cs` instead.
- **Impact**: The stated test topology and implied feature coverage are stale.
- **Recommended fix**: The schema-engine owner should implement the named tests
  or correct the inventory to the actual suite.
- **Ownership**: `src/Sanare.Core/Schema/**` is owned by the schema-engine
  component; this is report-only.
- **Resolution**: `tests/Sanare.Core.Tests/Schema/` now contains all five named
  files (`SchemaDeriverTests.cs`, `SchemaHasherTests.cs`,
  `SchemaValidatorTests.cs`, `TypeCoercerTests.cs`,
  `QualityReportBuilderTests.cs`), matching `schema-engine.md`'s test-module
  inventory exactly. No further action needed.

#### MIN-004: Plan-runtime implementation status is stale — ✅ RESOLVED 2026-09-10

- **Location**: `docs/features/overview.md` — Plan Runtime row.
- **Issue**: The implementation order marks `plan-runtime` as `draft`, while
  `src/Sanare.Core/Runtime/` contains production `IPlanExecutor`,
  `PlanExecutor`, `ExtractionOutcome`, and document-adapter code.
- **Impact**: Consumers planning dependencies cannot tell that a partial runtime
  foundation already exists.
- **Recommended fix**: Mark the feature `partial`, or retain `draft` only with a
  clear note that the existing runtime is an intentionally incomplete foundation.
- **Ownership**: `src/Sanare.Core/Runtime/**` is outside this session's edit
  scope; this is report-only for the plan-runtime owner.
- **Resolution**: `docs/features/overview.md`'s Plan Runtime row is now marked
  `partial`, and `docs/features/plan-runtime.md` carries an explicit
  "Implementation status: partial" callout describing the minimal v0.1 HTML
  interpreter foundation. See NEW-007 below for the accompanying documented-
  scope-overstatement finding that remains open.

#### MIN-005: Root development TODO still says Git-backed storage is unimplemented — ✅ RESOLVED (confirmed 2026-09-10)

- **Location**: `DEVELOPMENT.md` — TODO item 2.
- **Issue**: The item says to implement persistent/Git-backed extraction-plan
  storage and resolution while replacing `InMemoryExtractionPlanProvider`, but
  the repository now has the documented partial script-repository and
  plan-resolver implementation plus direct coverage recorded in the historical
  audit above.
- **Impact**: The root development checklist gives an obsolete starting point
  and obscures remaining work.
- **Recommended fix**: Replace the item with the specific remaining work, or
  mark the completed storage/resolution slice done and create follow-up items
  for the intended next capabilities.
- **Resolution**: Confirmed `DEVELOPMENT.md`'s Git-backed storage item is
  already checked off (`[x]`) as of this pass. No further action needed.

### Confirmed Current Alignment

- `docs/features/plan-resolver.md` accurately describes the current partial
  implementation and its `SNR-GIT-*` behavior.
- The other draft feature specifications are consistent with their stated
  unimplemented scope; no broken feature-spec links or placeholder-only sections
  were found.
- `README.md`, `AGENTS.md`, and the historical/provenance material under
  `ideas/sanare/` do not contradict the active code or feature-spec status.

### Recommended Priority Actions

1. **Resolve MAJ-003 first** — implement the extraction-plan validator and its
   tests, or reduce the spec to the serializer-only implemented slice.
2. **Synchronize status and test inventories** — address MIN-001 through
   MIN-005 when their owning component scopes are next active.
3. **Re-run this audit after the owners update their specs or implementations**
   to verify that the status table, test inventory, and implementation scope
   remain aligned.

### Full-Repository Audit Validation

- Ran a docs-only inventory across the repository and checked the active
  feature-spec set for link and status consistency.
- Cross-referenced the findings above against the current source and test tree,
  including direct confirmation that no `IPlanValidator` implementation exists
  and that `src/Sanare.Core/Runtime/` contains production runtime types.
- This pass was documentation-only. No build, test, or formatter run was needed
  because no code was changed.

## Full-Repository Findings — 2026-09-10

**Scope**: Re-verified every row of `docs/features/overview.md`'s 18-row
implementation-status table against the current `src/` and `tests/` trees, and
re-checked all six open findings from the 2026-09-06 pass. Updated status
markers and added per-feature "Implementation status" callouts where stale;
flagged newly discovered code/doc mismatches for a later, code-focused turn.
**Method**: Code-grounded cross-reference using symbolic search (Serena) and
direct file inspection. No production code was changed in this pass.

### Findings Summary (current)

| Severity | Open | Resolved this pass | Category |
|----------|-----:|--------------------:|----------|
| Critical | 0 | 0 | — |
| Major | 1 | 0 | Implemented-scope mismatch |
| Minor | 2 | 5 | Status and test-inventory staleness |
| Info | 0 | 0 | — |

MAJ-003 and MIN-002 remain open and unchanged from the 2026-09-06 pass, and NEW-006 remains open as a new finding from this pass (see
above) — they are report-only for their respective component owners and
were not in this pass's docs-update scope. MIN-001, MIN-003, MIN-004, MIN-005,
and NEW-007 are now resolved (see updated entries above); MIN-001's resolution
also added a unit test closing its underlying `SNR-GIT-004` coverage gap, and
NEW-007's resolution clarified the v0.1 vs. target-state scope split in
`plan-runtime.md`. One new Minor finding (NEW-006) remains open below.

### Doc Status Corrections Made This Pass

- **`docs/features/overview.md`** — Schema Engine (`#2`) status corrected
  `draft` → `implemented`: `src/Sanare.Core/Schema/` contains every file named
  in `schema-engine.md`'s File Structure section (`SchemaDeriver`,
  `SchemaHasher`, `SchemaValidator`, `Coercion/*`, `Quality/QualityReportBuilder`,
  `Materialization/*`), and `tests/Sanare.Core.Tests/Schema/` contains all five
  named test files (confirms MIN-003 resolution).
- **`docs/features/overview.md`** — Plan Runtime (`#9`) status corrected
  `draft` → `partial`: `src/Sanare.Core/Runtime/` contains production
  `IPlanExecutor`, `PlanExecutor`, `ExtractionOutcome`, and
  `Documents/HtmlDocument`, matching `PlanExecutor.cs`'s own doc-comment that
  describes a deliberately minimal v0.1 operation subset (confirms MIN-004
  resolution).
- **`docs/features/overview.md`** — "Updated:" date bumped to 2026-09-10.
- **`docs/features/schema-engine.md`** and **`docs/features/plan-runtime.md`**
  — added "Implementation status" callout lines (matching the pattern already
  used by `script-repository.md` and `observability.md`) so each spec states
  its current implementation state inline, not only in this report.
- All other 16 rows of `docs/features/overview.md` were re-verified against
  the source/test trees and found to already be accurate: `#1` Scrape API
  Contracts (`draft` — `IScraperAdministration`/`IFixtureAdministration` and a
  frozen public-API CI baseline are still fully absent), `#3` Extraction Plan
  Model (`draft` — no validator, per open MAJ-003), `#4` Script Repository /
  `#11` Plan Resolver (`partial`, unchanged), `#5` Fixture Corpus
  (`implemented`, unchanged), `#6` Acquisition Pipeline (`partial`,
  unchanged), `#7` Browsing Identity, `#8` Browser Tier, `#12` Authoring
  Workflow, `#13` Agent Toolset, `#14` Quality Evaluator, `#15` Healing
  Workflow, `#17` Hosting & Configuration, `#18` Lenovo Sample App (all
  `draft`, unchanged — no matching source files exist for any of these
  components), `#10` Pagination Engine (`draft`, unchanged —
  `NotSupportedException` is still thrown for pagination in
  `PlanExecutor.cs`/`FixtureScrapeRunner.cs`), `#16` Observability (`partial`,
  unchanged).

### New Minor Findings

#### NEW-006: Schema-engine materialization uses reflection, not the documented source-generation path

- **Location**: `docs/features/schema-engine.md` — Scope (materialization
  bullet), the "Materialise" pipeline step, Dependencies, and Constraints
  sections all describe `System.Text.Json` source-generation-based
  materialization with a reflection fallback that is "guarded and warns under
  trimming."
- **Issue**: `src/Sanare.Core/Schema/Materialization/DocumentMaterializer.cs`
  is unconditional reflection only (`Activator.CreateInstance`,
  `PropertyInfo.SetValue`); its own doc-comment calls it a "Simple reflection
  materializer for writable v0.1 schema properties." There is no
  `JsonSerializerContext`-based source-gen path and no trim/AOT guard or
  warning anywhere in the materialization code. A repository-wide search
  confirms the only `JsonSerializerContext` in the codebase
  (`AuditEventJsonContext.cs`) belongs to Observability/Audit and is unrelated
  to schema materialization.
- **Impact**: The spec's stated trimming/AOT safety story does not match the
  implementation. A consumer relying on the documented guard/warning behavior
  under trimming would find none exists; reflection-only materialization may
  also fail silently or behave unexpectedly in trimmed/AOT-published
  applications, which the spec explicitly claims is handled.
- **Recommended fix**: Either implement the source-generation path with the
  described trim/AOT guard and warning, or update `schema-engine.md`'s Scope,
  pipeline-step, Dependencies, and Constraints sections to describe the actual
  reflection-based materializer and its trimming/AOT limitations.
- **Ownership**: `src/Sanare.Core/Schema/Materialization/**` is owned by the
  schema-engine component; this is report-only, flagged for a later
  code-focused turn.

#### NEW-007: Plan-runtime documented file structure and test inventory substantially exceed the actual v0.1 implementation — ✅ RESOLVED 2026-09-10

- **Location**: `docs/features/plan-runtime.md` — File Structure section
  (Operations/Selectors, Transforms, Structure, Predicates, Locators, Budgets
  subdirectories) and Test Module section (`PlanExecutorTests.cs` plus five
  named companion files: `OperationTests.cs`, `LocatorChainTests.cs`,
  `CoercionMatrixTests.cs`, `StructuredDataViewTests.cs`,
  `DeterminismTests.cs`).
- **Issue**: `src/Sanare.Core/Runtime/` contains only four top-level items
  (`IPlanExecutor.cs`, `PlanExecutor.cs`, `ExtractionOutcome.cs`,
  `Documents/HtmlDocument.cs`) — none of the documented Operations, Locators,
  or Budgets subdirectories exist. `tests/Sanare.Core.Tests/Runtime/` contains
  only `FixtureScrapeRunnerTests.cs`; none of the six named test files exist.
- **Impact**: The spec reads as though the full runtime (structured-data
  adapters, the full operation allow-list, locator chains, pagination
  budgets, and a dedicated executor test suite) is already built, when the
  actual implementation is an intentionally minimal, HTML-only v0.1
  interpreter — `PlanExecutor.cs`'s own doc-comment states that "acquisition,
  browser operations, collections, and operations outside this subset are
  intentionally deferred." This is a genuine partial foundation, not a gap to
  close immediately, but the doc does not clearly separate target-state scope
  from what is built today.
- **Recommended fix**: Split `plan-runtime.md`'s File Structure and Test
  Module sections into "Implemented (v0.1)" and "Planned / target state"
  subsections (or move the target-state material to a follow-on milestone
  note), so the spec accurately reflects the current minimal interpreter
  versus the eventual full-scope runtime.
- **Ownership**: `src/Sanare.Core/Runtime/**` is owned by the plan-runtime
  component; this is report-only, flagged for a later code-focused turn.
- **Resolution**: `docs/features/plan-runtime.md`'s File Structure and Test
  Module sections are now each split into "Implemented (v0.1)" and "Planned /
  target-state (not yet built)" subsections. The Implemented subsections list
  only the four files that actually exist under `src/Sanare.Core/Runtime/`
  and describe `FixtureScrapeRunnerTests.cs`'s actual test scope; the Planned
  subsections retain the original target-state Operations/Locators/Budgets
  tree and `PlanExecutorTests.cs` companion-file inventory, now explicitly
  labeled as not yet built. No implementation was added — this is a
  documentation-only clarification.

### Recommended Priority Actions (2026-09-10 update)

1. **Resolve MAJ-003 first** — implement the extraction-plan validator and its
   tests, or reduce the spec to the serializer-only implemented slice
   (unchanged from the 2026-09-06 pass).
2. **Decide schema-engine's materialization strategy (NEW-006)** — either
   implement the documented source-generation/trim-guard path or correct the
   spec to describe the actual reflection-based implementation.
3. ~~**Split plan-runtime.md into implemented vs. target-state sections
   (NEW-007)**~~ — ✅ resolved 2026-09-10; the File Structure and Test Module
   sections are now split into "Implemented (v0.1)" and "Planned /
   target-state" subsections.
4. **Synchronize remaining status and test inventories** — address MIN-002
   when its owning component (scrape-api-contracts) is next active. MIN-001
   (script-repository) is resolved.
5. **Re-run this audit after the owners update their specs or implementations**
   to verify that the status table, test inventory, and implementation scope
   remain aligned.
