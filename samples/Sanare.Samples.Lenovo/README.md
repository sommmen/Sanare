# Sanare.Samples.Lenovo

A runnable proof that Sanare's engine, not hand-written scraping code, produces
schema-valid JSON for two real Lenovo NL pages:

1. **Tablet lister** — `https://www.lenovo.com/nl/nl/tablets/`
2. **Product detail** — `https://www.lenovo.com/nl/nl/p/tablets/android-tablets/lenovo-tab-series/lenovo-yoga-tab-gen-2/len103y0003`

The two source ids (`lenovo-com/tablet-lister`, `lenovo-com/tablet-detail`) and the
two URLs above are the only Lenovo-specific facts allowed in this project's C#. Every
CSS selector, XPath expression, JSON path, and content pattern used to pull data out of
the pages lives in the committed extraction-plan JSON under
`samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/`, not in code.
`tests/Sanare.Samples.Lenovo.Tests/ArchitectureGuardTests.cs` scans every `.cs` file in
this project and fails the build if that boundary is ever crossed.

## Commands

Run from the repository root:

```powershell
dotnet run --project samples/Sanare.Samples.Lenovo -- detail --offline
dotnet run --project samples/Sanare.Samples.Lenovo -- list --offline
dotnet run --project samples/Sanare.Samples.Lenovo -- validate
```

- `detail --offline` — executes the `tablet-detail` plan against the committed
  fixture and prints one `TabletProduct` JSON object to stdout: name, decimal price,
  `EUR` currency, availability, part number, images, and the full 14-row/5-group
  specification table.
- `list --offline` — executes the `tablet-lister` plan against the committed fixture
  and prints a JSON array of `TabletListing` objects (deduplicated by `ProductUrl`) to
  stdout.
- `validate` — runs `PlanValidator` over both committed plans against their derived
  schemas and prints a JSON array of `{ planSourceId, isValid, defects }` reports to
  stdout. Exits non-zero if either plan fails validation.

All diagnostics, warnings, and errors go to **stderr**. **stdout carries JSON only**,
so command output can always be piped straight into `ConvertFrom-Json` / `jq`.

## The offline guarantee

`--offline` reads only committed fixture files through `FileFixtureContentProvider`,
which maps `(sourceId, url)` to a file via the data-only manifest at
`samples/Sanare.Samples.Lenovo.State/fixtures/manifest.json`. No socket is ever opened
in this mode — there is no HTTP client in the offline code path at all. The two source
fixtures are:

- `fixtures/lenovo-com/yoga-tab-gen2-detail.html`
- `fixtures/lenovo-com/tablets-lister-page1.html`

Golden outputs for both commands are committed under
`samples/Sanare.Samples.Lenovo.State/golden/` and are asserted against by the
integration tests in `tests/Sanare.Samples.Lenovo.Tests/`.

## Where the plans live

- `samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/tablet-detail.json`
- `samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/tablet-lister.json`

Both are hand-written `Tier = StructuredData` plans (hand-authoring is explicitly
permitted for v0.1 fixtures — there is no LLM/`IChatClient` call anywhere in this
sample). The detail plan pulls name, price, currency, availability, part number, and
images from the page's `application/ld+json` Product block, and the 14 specification
rows from the inline `$pdpAllData` JSON island, using only the closed
`PlanOperation` vocabulary (`SelectFirst`, `RegexCapture`, `JsonPath`, `Html`, and
related transforms). The lister plan resolves tablet listings from the page's
embedded navigation JSON.

## Refreshing fixtures manually

There is no live-capture mode in this sample (no network access is part of the
product claim for `--offline`). To refresh a fixture:

1. Save the updated page HTML to the matching path under
   `samples/Sanare.Samples.Lenovo.State/fixtures/lenovo-com/`.
2. Re-run `dotnet run --project samples/Sanare.Samples.Lenovo -- detail --offline` (or
   `list --offline`) and diff the output against the committed golden file.
3. If the page structure changed enough that the committed plan no longer matches,
   update the plan JSON by hand — selectors and paths belong only there, never in
   `Program.cs` or the schema types.
4. Re-run `validate` to confirm the (possibly edited) plan still matches its schema.
