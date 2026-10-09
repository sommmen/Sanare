# Plan: migrate tests to `xunit.v3.mtp-v2` 4.0.1

Migrates all five test projects from xUnit v2 on VSTest to xUnit v3 on Microsoft
Testing Platform v2, aligning this repository with the estate-wide test standard.

The shared standard, target recipe and CI command reference live in the `stallions`
repository at `docs/guides/Testing/xunit-v3-mtp-v2-standard.md`.

This repository has the **oldest test pins in the estate** (`xunit` 2.9.2,
`xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.12.0) and two
constructs that genuinely break: an `IAsyncLifetime` fixture and a trait-based CI
filter.

## 1. Current state

Five test projects, all under `tests/`:

| Project | Extra packages | Notable |
|---|---|---|
| `Sanare.Abstractions.Tests` | `PublicApiGenerator` 11.5.4 | approved-API `.approved.txt` content |
| `Sanare.Browser.Tests` | — | `FrameworkReference Microsoft.AspNetCore.App`, `TestSite/**` content, Playwright |
| `Sanare.Core.Tests` | — | |
| `Sanare.Http.Tests` | `PublicApiGenerator` 11.5.4 | approved-API content |
| `Sanare.Samples.Lenovo.Tests` | — | |

Shared package set in every project:

| Package | Version | Fate |
|---|---|---|
| `xunit` | **2.9.2** | → `xunit.v3.mtp-v2` 4.0.1 |
| `xunit.runner.visualstudio` | **2.8.2** | remove (VSTest adapter) |
| `Microsoft.NET.Test.Sdk` | **17.12.0** | remove (VSTest host) |

Note this repository has **no** `coverlet.collector` — one fewer thing to remove than
the other xUnit v2 repositories.

Other facts:

- `Directory.Build.props` at the root sets `TargetFramework=net10.0`,
  `LangVersion=14.0`, `ImplicitUsings`, `Nullable`, `TreatWarningsAsErrors=true`,
  `AnalysisLevel=latest`, `EnableNETAnalyzers=true`, `IsPackable=false`.
- Each test project has `GlobalUsings.cs` containing `global using Xunit;`.
- No central package management, no `global.json`.
- Solution: `Sanare.slnx`. CI: `.github/workflows/ci.yml`.

### Migration surface, measured

| Construct | Count | Impact |
|---|---:|---|
| `[Fact]` | 507 | none |
| `[Theory]` | 44 | none |
| `[InlineData]` | 151 | none |
| `[MemberData]` | 1 | none |
| `Assert.ThrowsAsync` | 45 | none |
| `[CollectionDefinition]` / `[Collection]` | 1 / 3 | none |
| `IClassFixture` | 2 | none |
| `IAsyncLifetime` | 1 | **breaking — §2.1** |
| `[Trait("Category", "Browser")]` | 4 | CI filter must change — §2.2 |
| `async void` in test code | 0 | none |

88 test source files. The single `async void` in the repository is in *production*
code (`src/Sanare.Browser/NetworkLogRecorder.cs:50`, a Playwright event handler) and is
unaffected by the migration.

## 2. The two things that actually break

### 2.1 `BrowserTestSiteFixture` — `IAsyncLifetime` returns `ValueTask`

`tests/Sanare.Browser.Tests/BrowserTestSiteFixture.cs:12` implements `IAsyncLifetime`
with the v2 signatures:

```csharp
public async Task InitializeAsync() { ... }
public async Task DisposeAsync() { ... }
```

In v3 the interface inherits `IAsyncDisposable`, so both must return `ValueTask`:

```csharp
public async ValueTask InitializeAsync() { ... }
public async ValueTask DisposeAsync() { ... }
```

The method bodies are unchanged — `async ValueTask` behaves identically for these.
This is a compile error (`CS0738`), not a silent change, so it cannot be missed.

The fixture starts a real Kestrel host on a loopback port and is consumed through
`IClassFixture<BrowserTestSiteFixture>` by two test classes. `IClassFixture` semantics
are unchanged in v3, so once the signatures are fixed the fixture behaves as before.
Verify after migration that the host is actually stopped — a `DisposeAsync` that stops
being called would leak a listening port per test class, which shows up as a slow or
hanging run rather than a failure.

### 2.2 The `Category!=Browser` CI filter

`.github/workflows/ci.yml` runs:

```
dotnet test --no-build --no-restore --configuration Release --verbosity normal --filter "Category!=Browser"
```

The comment above it explains why: `[Trait("Category", "Browser")]` tests launch a real
Chromium instance through Playwright, which is not installed on the runner.

Two options:

**Keep VSTest filter syntax.** `--filter "Category!=Browser"` continues to work under
MTP — verified on SDK 10.0.401, where a VSTest-syntax filter correctly excluded a
`[Trait]`-tagged test from an `xunit.v3.mtp-v2` project. Zero-change path.

**Move to native MTP filtering (preferred).** Clearer, and what the standard
recommends:

```
dotnet test --no-build --no-restore --configuration Release -- --filter-not-trait "Category=Browser"
```

Framework options must come after `--`. Whichever is chosen, **verify the excluded
count explicitly**: if the filter silently stops matching, the Browser tests start
running on a runner with no Chromium and the job fails confusingly — or worse, the
filter over-matches and silently skips real tests. Compare the executed test count
before and after.

The four `[Trait("Category", "Browser")]` sites are in
`BrowserContentAcquirerIntegrationTests.cs`, `BrowserPoolIntegrationTests.cs`,
`PlaywrightChallengeHandoffIntegrationTests.cs` and
`TieredContentAcquirerIntegrationTests.cs`.

## 3. Changes

### 3.1 Add test properties

All five projects need `OutputType=Exe` and `TestingPlatformDotnetTestSupport=true`.
The root `Directory.Build.props` covers both `src/` and `tests/`, so these must **not**
go there unconditionally — `OutputType=Exe` would turn every library into an
executable.

Add `tests/Directory.Build.props`:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove($(MSBuildThisFile), $(MSBuildThisFileDirectory)..))" />

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>

    <!-- Temporary: xunit.analyzers 2.1.0 fires xUnit1051 across the suite and the root
         props sets TreatWarningsAsErrors. Suppress here to land the framework migration
         as a reviewable change, then remove this line and fix the sites. See §5. -->
    <NoWarn>$(NoWarn);xUnit1051</NoWarn>
  </PropertyGroup>
</Project>
```

The `Import` is required: MSBuild stops at the nearest `Directory.Build.props`, so
without it the five test projects would lose `TargetFramework`, `Nullable`,
`TreatWarningsAsErrors` and the rest from the root file.

### 3.2 Each of the five test project files

Remove `xunit`, `xunit.runner.visualstudio` and `Microsoft.NET.Test.Sdk`. Add:

```xml
<PackageReference Include="xunit.v3.mtp-v2" Version="4.0.1" />
<PackageReference Include="Microsoft.Testing.Extensions.GitHubActionsReport" Version="2.4.1" />
```

Keep everything else: `PublicApiGenerator` in the two API-surface projects, the
`FrameworkReference` and `TestSite/**` content in `Sanare.Browser.Tests`, the
`.approved.txt` content items, `IsTestProject=true`, and all `ProjectReference`s.

`GlobalUsings.cs` already contains `global using Xunit;` in all five projects and needs
no change — the namespace is the same in v3.

### 3.3 New `global.json`

At the repository root, next to `Sanare.slnx`:

```json
{
  "sdk": {
    "version": "10.0.401",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  },
  "test": {
    "runner": "Microsoft.Testing.Platform"
  }
}
```

### 3.4 CI

Update the filter per §2.2. Optionally add `--report-gh` (enabled by the extension
package in §3.2) and bump `actions/checkout@v4` → `@v7` and
`actions/setup-dotnet@v4` → `@v6` to match the estate.

## 4. Execution order

| # | Step | Why this position |
|---|---|---|
| 1 | `tests/Directory.Build.props` + `Sanare.Core.Tests` | Proves the props-import and package recipe on a plain project |
| 2 | `Sanare.Samples.Lenovo.Tests` | Second plain project |
| 3 | `Sanare.Abstractions.Tests` + `Sanare.Http.Tests` | `PublicApiGenerator` + `.approved.txt`; confirm approved-API tests still find their files |
| 4 | `Sanare.Browser.Tests` + the `IAsyncLifetime` fix | The only source change |
| 5 | `global.json` + CI filter | Switch the runner and filter together, once everything builds |

## 5. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| **`TreatWarningsAsErrors=true` + `xUnit1051`** | **Certain** | See the note below — this is the largest single risk in this plan |
| `tests/Directory.Build.props` without the `Import` | Medium | Test projects silently lose `TargetFramework`/`Nullable`/analyzers; §3.1 |
| `IAsyncLifetime` signatures | Certain | Compile error; §2.1 |
| Browser filter mis-targets | Medium | Compare executed counts before/after; §2.2 |
| `.approved.txt` files not copied | Low | `CopyToOutputDirectory="PreserveNewest"` is unchanged, but `OutputType=Exe` changes the output layout — run the two API-surface test classes explicitly |
| Zero discovery | Medium | Missing `OutputType=Exe`; compare per-project counts |

### The `xUnit1051` problem

`xunit.v3.mtp-v2` 4.0.1 brings `xunit.analyzers` 2.1.0, which fires `xUnit1051`:
"calls to methods which accept `CancellationToken` should use
`TestContext.Current.CancellationToken`". A trial migration of a comparable repository
(`dotnet-efcore-mcp`, 546 tests) produced **150 `xUnit1051` warnings** and no other new
rule.

The root `Directory.Build.props` here sets **`TreatWarningsAsErrors=true`**, so in this
repository those warnings are **build errors**. With 88 test files and 45
`Assert.ThrowsAsync` sites, expect a comparable volume.

Decide the approach before starting:

- **Recommended:** add `<NoWarn>$(NoWarn);xUnit1051</NoWarn>` to
  `tests/Directory.Build.props` (§3.1) so the migration lands as a reviewable
  packaging change, then remove the suppression and fix the sites as dedicated
  follow-up work. Passing `TestContext.Current.CancellationToken` is a genuine
  improvement, so it is worth doing — just not in the same commit as a framework swap.
- **Alternative:** fix every site during the migration. Correct, but it produces a very
  large diff in which a real regression is easy to miss.

Do **not** discover this mid-migration — with warnings-as-errors it will stop the build
on the first project.

## 6. Verification

```powershell
dotnet restore
dotnet build --no-restore --configuration Release
dotnet test --no-build --no-restore --configuration Release --verbosity normal
```

Then the filtered form CI uses, to confirm the Browser exclusion still works.

Definition of done:

1. No `xunit` (v2), `xunit.runner.visualstudio` or `Microsoft.NET.Test.Sdk` reference
   remains.
2. All five projects reference `xunit.v3.mtp-v2` 4.0.1.
3. The unfiltered test count matches the pre-migration baseline, **and** the filtered
   count excludes exactly the same Browser tests as before — capture both baselines
   first.
4. `ci` green on the pull request.
