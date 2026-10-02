# Ralph Plan — Finish Sanare and ship the Lenovo demo

> Goal: close the remaining `draft`/`partial` gaps that the working end-to-end demo
> actually requires, then land `samples/Sanare.Samples.Lenovo` as a runnable console
> application that prints schema-valid JSON for the Lenovo Yoga Tab Gen 2 detail page
> **offline, with zero network access**, driven entirely by a versioned extraction plan.

## Definition of done

All of the following must hold at once:

1. `dotnet build Sanare.slnx -c Release` exits 0 with **zero warnings**
   (`TreatWarningsAsErrors` is already on; do not weaken it).
2. `dotnet test Sanare.slnx -c Release --filter "Category!=Browser"` exits 0.
3. `dotnet run --project samples/Sanare.Samples.Lenovo -- detail --offline`
   exits 0 and writes a single JSON object to **stdout** that contains the product
   name, a decimal price, currency `EUR`, availability, and **at least 14**
   specification rows.
4. `dotnet run --project samples/Sanare.Samples.Lenovo -- list --offline`
   exits 0 and writes a JSON array of tablet listings to stdout.
5. `dotnet run --project samples/Sanare.Samples.Lenovo -- validate`
   exits 0 and reports both checked-in plans as structurally valid.
6. Every checklist task below is checked off.

Ground truth for 3–5 is the process exit code plus the parsed stdout, not narration.

## Quality bars — do not cross these

- **Do not weaken, delete, skip, or `[Fact(Skip=...)]` any existing test.** The repo
  starts at 611 passing tests; the count must never go down.
- **Do not suppress warnings** with `#pragma warning disable`, `<NoWarn>`, or by
  lowering `TreatWarningsAsErrors`. Fix the cause.
- **Do not put Lenovo CSS selectors, XPath, JSON pointers, regexes, or URLs-to-content
  mappings in sample C# code.** They belong only in the JSON extraction plans under
  `samples/Sanare.Samples.Lenovo.State/scripts/plans/`. The sample's C# may name the two
  source ids and the two start URLs — nothing more. A test must enforce this.
- **The `--offline` path must perform zero DNS and zero socket connections.** It reads
  only the checked-in fixture files.
- **Do not call any LLM or `IChatClient` at runtime.** Plan authoring/healing is out of
  scope for this plan; the checked-in plans are hand-written, which the spec explicitly
  permits for v0.1 fixtures.
- **Do not add new NuGet packages** unless a task below names one. Everything needed is
  already referenced (AngleSharp, AngleSharp.XPath, System.Text.Json, LibGit2Sharp).
- Keep `Sanare.Abstractions` dependency-free; there is an API-surface test guarding it.
  If you intentionally change the public surface, update the approved API text file in
  the same commit.
- Follow Conventional Commits (`feat(scope): ...`). Never add a `Co-authored-by` trailer.

## Ground truth already established (do not re-derive)

Real pages were captured and committed at
`samples/Sanare.Samples.Lenovo.State/fixtures/lenovo-com/`:

- `yoga-tab-gen2-detail.html` — the Yoga Tab Gen 2 product detail page.
- `tablets-lister-page1.html` — the `/nl/nl/tablets/` lister page.

Facts verified against those exact files:

- The detail page carries a **`application/ld+json` Product block** with
  `name` = `Lenovo Yoga Tab Gen 2`, `offers.price` = `649.01`,
  `offers.priceCurrency` = `EUR`, `offers.availability` =
  `http://schema.org/InStock`, `sku`/`mpn` = `LEN103Y0003`, and an `image` array.
  It is the **second** `ld+json` script in the document (the first is a breadcrumb
  `itemListElement` block), so a plan must select the Product block by content, not by
  ordinal position alone.
- The **entire specification table is embedded in the HTML** inside an inline
  `<script>` that begins `var $pdpAllData = {` and ends at the next `</script>`.
  Its `techSpecs.tables` is an array of `{ groupHeadline, specs: [{ headline, text }] }`.
  There are **5 groups and 14 spec rows**: Prestaties (3), Connectiviteit (3),
  Ontwerp (4), Duurzaamheid (1), Overige informatie (3). `text` contains HTML markup
  (`<ul>/<li>/<p>`) that must be flattened to readable text.
  **No browser is required** — ordinary HTTP acquisition sees all of it.
- The lister page is a marketing landing page; product links matching
  `/p/tablets/...` appear in an embedded navigation JSON. Treat the lister as a
  best-effort listing over the links present in that fixture. Do **not** go to the
  network to improve it.

Use these facts. Do not spend an iteration rediscovering them, and **never** fetch
lenovo.com during this plan.

## Checklist

- [ ] **T1 — Runtime: JSON-island + structured extraction primitives.**
  `PlanExecutor` currently supports only `SelectFirst`/`Text`/`Attribute` locators and
  `Trim`/`CollapseWhitespace`/`StripCurrency`/`StripUnit` transforms, and throws
  `NotSupportedException` for everything else. The demo cannot be expressed in that
  subset. Extend `HtmlDocument` and `PlanExecutor` to support, from the already-closed
  `PlanOperation` vocabulary: `XPath`, `RegexCapture`, `Html`, `JsonPath`, `ParseDecimal`,
  `ParseInt`, `ParseBool`, `MapEnum`, `Split`, `Index`, `Coalesce`, `Exists`, and
  `Concat`. `JsonPath` must be able to run against a JSON document extracted from the
  page by a preceding locator step (that is the `$pdpAllData` / `ld+json` case), so
  locator steps must thread an intermediate value rather than always restarting from the
  document. `RegexCapture` must keep the existing non-backtracking guarantee that
  `PlanValidator` enforces, and must be given a bounded `Regex` timeout.
  Add focused unit tests in `tests/Sanare.Core.Tests` for each new operation, including
  a negative test that an unsupported operation still fails loudly rather than silently
  yielding null. Depends on: —.

- [ ] **T2 — Runtime: collection extraction (`ExecuteMany`).**
  Add `IPlanExecutor.ExecuteMany(plan, content, schema)` returning one
  `ExtractionOutcome` per item, driven by `ExtractionPlan.Root` as the item locator, with
  per-item field pointers resolved relative to the item. Support a `Root` that selects
  over HTML elements **and** one that selects over a JSON array (needed for the 14 spec
  rows, which live in JSON, and for the lister). An item whose required fields are
  missing is reported as a failed item, not silently dropped. Add unit tests covering
  both shapes, empty collections, and `MaxItems` clamping. Depends on: T1.

- [ ] **T3 — Plan validation catches up with T1/T2.**
  Extend `PlanValidator` so the newly reachable operations validate correctly:
  arity/tier checks for each operation added in T1, and the rule that a plan with a
  non-`None` pagination strategy, or any field pointer containing `/*`, must declare a
  `Root`. Keep every existing validator test passing. Add tests for the new rules.
  Depends on: T2.

- [ ] **T4 — Offline fixture acquisition seam for the sample.**
  The sample must read the committed fixture HTML with **zero sockets**. Provide a
  file-backed `IFixtureContentProvider` in `Sanare.Core` (next to
  `InMemoryFixtureContentProvider`) that maps a `(sourceId, url)` pair to a file on disk
  via a small committed manifest JSON, so the mapping is data, not C# code. Unit-test it,
  including the miss path. Depends on: —.

- [ ] **T5 — Sample project skeleton.**
  Create `samples/Sanare.Samples.Lenovo/Sanare.Samples.Lenovo.csproj` (console,
  `net10.0`, `IsPackable=false`) referencing `Sanare.Core` and `Sanare.Abstractions`, and
  add it plus a `samples/` folder entry to `Sanare.slnx`. Add
  `tests/Sanare.Samples.Lenovo.Tests` and register it in `Sanare.slnx` too. The project
  must build clean with warnings-as-errors. At this point `Program.cs` may just print
  usage and return 0. Depends on: —.

- [ ] **T6 — Typed schemas and JSON output contract.**
  Add `TabletListing`, `TabletProduct`, and `ProductSpecification` records as described
  in `docs/features/sample-app-lenovo.md` ("Typed schemas"), plus a
  `System.Text.Json` source-generated `LenovoJsonContext`. stdout is JSON only; all logs
  and diagnostics go to **stderr**. Prices serialize as JSON numbers. Add unit tests for
  schema derivation of all three records and for the writer. Depends on: T5.

- [ ] **T7 — The two extraction plans, committed as data.**
  Hand-write canonical plan JSON under
  `samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/` — one for
  `lenovo-com/tablet-detail` and one for `lenovo-com/tablet-lister`. They must round-trip
  through `PlanSerializer` byte-identically and pass `PlanValidator`. The detail plan
  extracts name/price/currency/availability/part-number/images from the `ld+json` Product
  block and all 14 specification rows from the `$pdpAllData` JSON island, using only
  operations from the closed vocabulary. Add a test asserting round-trip stability and
  validity of both committed plans. Depends on: T3, T6.

- [ ] **T8 — Wire the sample end to end: `detail --offline`.**
  Implement the `detail` command: load the committed plan, derive the `TabletProduct`
  schema, read the fixture through T4's provider, execute via `PlanExecutor` +
  `ExecuteMany` for the specification collection, validate/materialize, and print JSON to
  stdout. Exit 0 on success; on failure print nothing to stdout, a diagnostic to stderr,
  and return a non-zero code. Write the golden output to
  `samples/Sanare.Samples.Lenovo.State/golden/yoga-tab-gen2.json`. Add an integration
  test that runs the command in-process and asserts: exit 0, parseable stdout, the
  expected name, price `649.01`, currency `EUR`, and **>= 14** specification rows with
  non-empty `Name` and `Value`. Depends on: T7, T4.

- [ ] **T9 — Wire the sample end to end: `list --offline` and `validate`.**
  Implement `list --offline` (streams tablet listings from the lister fixture as a JSON
  array, deduplicated by `ProductUrl`) and `validate` (validates both committed plans and
  reports per-plan results, exit 0 only when both are valid). Write
  `samples/Sanare.Samples.Lenovo.State/golden/tablet-list.json`. Add integration tests for
  both commands. Depends on: T8.

- [ ] **T10 — Architecture guard test.**
  Add a test that scans every `.cs` file under `samples/Sanare.Samples.Lenovo/` and fails
  if it contains CSS-selector-looking strings, XPath literals, JSON pointers into Lenovo
  payloads, or `lenovo.com` URLs other than the two declared start URLs. This is the test
  that proves the product claim. Depends on: T9.

- [ ] **T11 — Sample README and documentation reconciliation.**
  Write `samples/Sanare.Samples.Lenovo/README.md` documenting the commands, the offline
  guarantee, where the plans live, and how to regenerate fixtures manually. Update
  `README.md`, `DEVELOPMENT.md`, and `docs/features/overview.md` so the status table and
  todo list match what now actually exists — including flipping row 18 off `draft`. Do
  not claim anything the code does not do. Depends on: T10.

- [ ] **T12 — Final verification sweep.**
  Re-run the full definition of done from a clean build. Then grep the repository to
  prove the quality bars held: no new `#pragma warning disable`, no `NoWarn`, no
  `Skip =` added to tests, and no Lenovo selector strings in sample C#. Confirm the test
  count is **>= 611**. Record the actual numbers in Notes. Depends on: T11.

## Notes

Each iteration starts with a blank context; this section is the only memory between
iterations. Append findings here — what you changed, what surprised you, what the next
iteration should know. Keep it factual and short.

- (iteration notes go here)
