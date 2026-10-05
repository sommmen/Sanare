# Ralph Plan — Finish Sanare and ship the Lenovo demo

> Goal: a runnable console sample that prints schema-valid JSON for the Lenovo
> Yoga Tab Gen 2 detail page **offline, with zero network access**, driven entirely
> by a versioned extraction plan rather than by hand-written scraping code.

Each task below is sized for a **single 20-minute agent session**. Prefer finishing one
whole small task over surveying broadly — the survey is already written down below.

## Definition of done

1. `dotnet build Sanare.slnx -c Release` exits 0 with **zero warnings**.
2. `dotnet test Sanare.slnx -c Release --filter "Category!=Browser"` exits 0.
3. `dotnet run --project samples/Sanare.Samples.Lenovo -- detail --offline`
   exits 0 and writes one JSON object to **stdout** with the product name, a decimal
   price, currency `EUR`, availability, and **at least 14** specification rows.
4. `dotnet run --project samples/Sanare.Samples.Lenovo -- list --offline` exits 0 and
   writes a JSON array of tablet listings to stdout.
5. `dotnet run --project samples/Sanare.Samples.Lenovo -- validate` exits 0.
6. Every checklist task below is checked off.

## Quality bars — do not cross these

- **Never weaken, delete, or skip an existing test.** The repo starts at **611**
  passing non-Browser tests. That number must never go down.
- **Never suppress a warning** (`#pragma warning disable`, `<NoWarn>`, lowering
  `TreatWarningsAsErrors`). Fix the cause.
- **No Lenovo CSS selectors, XPath, JSON pointers, or content regexes in sample C#.**
  They live only in the plan JSON under
  `samples/Sanare.Samples.Lenovo.State/scripts/plans/`. Sample C# may name the two
  source ids and the two start URLs, nothing more. T13 enforces this with a test.
- **`--offline` must open zero sockets.** It reads only committed fixture files.
- **No LLM / `IChatClient` calls at runtime.** The plans are hand-written, which the
  spec explicitly permits for v0.1 fixtures.
- **No new NuGet packages.** AngleSharp, AngleSharp.XPath, System.Text.Json and
  LibGit2Sharp are already referenced and are sufficient.
- Keep `Sanare.Abstractions` dependency-free. If you deliberately change the public
  API surface, update the approved API text file in the same commit.
- Conventional Commits. **Never** add a `Co-authored-by` trailer.

## Established ground truth — use it, do not re-derive it

Two real pages are already committed under
`samples/Sanare.Samples.Lenovo.State/fixtures/lenovo-com/`:
`yoga-tab-gen2-detail.html` and `tablets-lister-page1.html`.
**Never fetch lenovo.com.** Verified facts about those exact files:

- The detail page has **two** `application/ld+json` scripts. The **second** is the
  Product block: `name` = `Lenovo Yoga Tab Gen 2`, `offers.price` = `649.01`,
  `offers.priceCurrency` = `EUR`, `offers.availability` = `http://schema.org/InStock`,
  `sku`/`mpn` = `LEN103Y0003`, `image` is an array of protocol-relative URLs
  (`//p3-ofp.static.pub/...`). Select it by `"@type":"Product"` content, not by index.
- The **whole specification table is in the HTML**, inside an inline `<script>` that
  starts with `var $pdpAllData = {` and runs to the next `</script>`. In that JSON,
  `techSpecs.tables` is an array of `{ groupHeadline, specs: [{ headline, text }] }`.
  There are **5 groups and 14 rows**: Prestaties 3, Connectiviteit 3, Ontwerp 4,
  Duurzaamheid 1, Overige informatie 3. `text` holds HTML (`<ul>/<li>/<p>`) that must be
  flattened to plain text. **No browser needed.**
- The lister fixture is a marketing page whose product links (`/p/tablets/...`) live in
  an embedded navigation JSON. Treat it as a best-effort listing over that fixture.

### A locator chain that is *proven* to work against the committed fixture

This exact `FieldPlan` was executed against `yoga-tab-gen2-detail.html` and returned
`Lenovo Yoga Tab Gen 2`. Reuse this shape for the scalar `ld+json` fields:

```
pointer:    /Name
locators:   Html                                      (0 args — whole document markup)
            RegexCapture  <script[^>]*application/ld\+json[^>]*>([^<]*"@type":"Product"[^<]*)</script>  1
            JsonPath      $.name
```

Swap the final `JsonPath` for `$.offers.price`, `$.offers.priceCurrency`,
`$.offers.availability`, `$.sku`, or `$.image[0]` for the other scalar fields.

For the specification rows, the `$pdpAllData` island must first be cut out of the page.
`ExecuteMany` parses its `content` argument as **raw JSON** when `Root` starts with `$`,
so pass the island JSON itself — *not* wrapped in a `<script>` tag. The island is the
text between `var $pdpAllData = ` and the next `</script>`, trimmed of a trailing `;`.
`ExecuteMany` wraps each JSON item back into `<script class="sanare-json-item">…</script>`
before running `Execute` on it, and a **first** locator step always runs against that
document. `JsonPath` is therefore *not* valid as a first step — it is a value-kind
operation and throws `NotSupportedException` there. The working item-field shape is to
select the wrapper's text first and then apply `JsonPath` to it.

**Verified end to end against the real fixture — this produced all 14 rows:**

```
root:       $.techSpecs.tables[*].specs[*]
content:    the raw $pdpAllData island JSON (NOT wrapped in a script tag)

pointer:    /Specifications/*/Name
locators:   SelectFirst  script.sanare-json-item
            JsonPath     $.headline

pointer:    /Specifications/*/Value
locators:   SelectFirst  script.sanare-json-item
            JsonPath     $.text
```

`Value` comes back as HTML (`<ul><li><p>…`), so the field needs the HTML-flattening
transform from T4 to become readable text. `Group` comes from the enclosing
`groupHeadline`, which a flat `[*].specs[*]` root loses — either extract groups with a
second pass over `$.techSpecs.tables[*]`, or accept `Group` as null and say so in Notes.

## Known code constraints — read before writing code

- `PlanExecutor.Locate` supports only `SelectFirst`/`Text` (1 arg) and `Attribute`
  (2 args), and **throws `NotSupportedException`** for anything else.
  `TryTransform` supports only `Trim`, `CollapseWhitespace`, `StripCurrency`,
  `StripUnit`. Everything else must be added.
- `PlanValidator` already validates arity and tier **generically** from
  `PlanOperationCatalog`, so newly-supported operations need **no** new validator rules.
  Do not add redundant ones.
- Operation tiers in the catalog are fixed: `JsonPath` is allowed only for tiers
  `JsonApi`/`StructuredData`; `SelectFirst`/`SelectAll`/`XPath` only for
  `StructuredData`/`Html`/`Browser`. **A plan using both CSS and `JsonPath` must declare
  `Tier = StructuredData`.** That is the tier the detail plan must use.
- `DocumentMaterializer` uses `Activator.CreateInstance<T>()` plus **writable**
  properties keyed by `"/" + PropertyName`. It cannot populate positional records.
  Sample schema types must therefore have a parameterless constructor and settable
  properties, or the materializer must be upgraded. Pick one; record it in Notes.
- `SchemaDeriver` emits pointers like `/Specifications/*/Name` for a collection of
  complex elements, and requires exactly one `[ScrapeCollection]` property per schema.
- There is **no** `tests/Sanare.Core.Tests/Runtime/` folder yet; create it.

## Checklist

- [x] **T1 — Thread an intermediate value through locator steps.**
  Today every locator step restarts from the document, so `JsonPath` can never run
  against JSON pulled out of the page. Refactor `PlanExecutor.Locate` so a field's
  `Locators` list is a **pipeline**: step 1 reads the document, each later step receives
  the previous step's string output. Preserve today's behaviour exactly for single-step
  fields and for existing fallback-locator semantics. Resolve the ambiguity this way and
  state it in a code comment: steps after the first that are *value-kind* operations
  (`JsonPath`, `RegexCapture`, `Index`, `Split`, `Text`, `Attribute`) consume the
  previous value; a repeat of a *document* locator (`SelectFirst`/`SelectAll`/`XPath`)
  is an alternative and restarts from the document. Add
  `tests/Sanare.Core.Tests/Runtime/PlanExecutorTests.cs` covering both shapes. Depends on: —.

- [x] **T2 — Add `XPath`, `Html`, and `RegexCapture` locators.**
  `HtmlDocument` gains `SelectXPath(expr)` (AngleSharp.XPath is referenced) and
  `OuterHtml`. `PlanExecutor` supports `XPath` (1 arg), `Html` (0 args — yields the
  document or the piped value as HTML), and `RegexCapture` (pattern, optional group
  index) compiled with `RegexOptions.NonBacktracking` and a **1-second timeout**,
  matching what `PlanValidator` already promises. Unit-test each, including a
  `RegexCapture` miss returning null rather than throwing. Depends on: T1.

- [x] **T3 — Add the `JsonPath` locator.**
  Support a **documented, deliberately small** path subset over a JSON string produced
  by a previous step: `$.a.b`, `$.a[0].b`, and `$.a[*].b` returning the first match for
  scalar use. `System.Text.Json` only. An invalid path or a miss yields null plus a
  diagnostic — never an escaping exception. Put the supported grammar in an XML doc
  comment. Unit-test hits, misses, nested arrays, and malformed JSON. Depends on: T2.

- [x] **T4 — Transform operations the demo needs.**
  Extend `PlanExecutor.TryTransform` with `Split` (1 arg), `Index` (1 int arg),
  `Concat` (0–1 separator), `Coalesce`, `Exists`, `MapEnum` (pairs), `ParseInt`,
  `ParseDecimal`, `ParseBool` — all already in the closed vocabulary, all with the arity
  the catalog declares. Final CLR coercion stays `TypeCoercer`'s job; these operate on
  text. Also add HTML-to-readable-text flattening, which the spec `text` values need;
  name it clearly and say in Notes which operation exposes it. Unit-test each. Depends on: T3.

- [x] **T5 — `ExecuteMany` for collections.**
  Add `ExtractionOutcome[] ExecuteMany(plan, content, schema)` to `IPlanExecutor` and
  `PlanExecutor`, driven by `ExtractionPlan.Root` as the item locator, with field
  pointers containing `/*` resolved **relative to each item**. Support a `Root` over
  HTML elements *and* over a **JSON array** (the 14 spec rows are JSON). Items missing
  required fields are reported as failed items, never dropped silently. Honour
  `Pagination.MaxItems` as a clamp when set. Unit-test both shapes, the empty
  collection, and the clamp. Depends on: T4.

- [x] **T6 — File-backed offline fixture provider.**
  Add `FileFixtureContentProvider` to `src/Sanare.Core/Fixtures/` implementing
  `IFixtureContentProvider`, resolving `(sourceId, url)` to a file through a small
  committed manifest JSON so the mapping is **data, not code**. Zero sockets by
  construction. Unit-test a hit, a miss, and a manifest entry pointing at a missing
  file. Depends on: —.

- [x] **T7 — Sample and test projects exist and build.**
  Create `samples/Sanare.Samples.Lenovo/Sanare.Samples.Lenovo.csproj` (console,
  `net10.0`, `IsPackable=false`) referencing `Sanare.Core` + `Sanare.Abstractions`, and
  `tests/Sanare.Samples.Lenovo.Tests`. Register both in `Sanare.slnx` under a new
  `/samples/` folder and the existing `/tests/` folder. `Program.cs` may print usage and
  return 0 for now. Build must be clean under warnings-as-errors. Depends on: —.

- [x] **T8 — Typed schemas + JSON output contract.**
  Add `TabletListing`, `TabletProduct`, `ProductSpecification` (shapes in
  `docs/features/sample-app-lenovo.md` → "Typed schemas"), honouring the materializer
  constraint recorded in Notes. Add a source-generated `LenovoJsonContext`. **stdout is
  JSON only; every log and diagnostic goes to stderr.** Prices are JSON numbers.
  Unit-test schema derivation for all three types and the stdout/stderr split. Depends on: T7.

- [x] **T8b — Fix the two `ExecuteMany` bugs found by running it against the real fixture.**
  Both are proven by execution, not by reading; fix them before attempting T9.
  1. **Pointer collision.** `ExecuteMany` relativizes *all* schema fields, so
     `/Specifications/*/Name` becomes `/Name` and collides with `TabletProduct`'s own
     top-level `/Name`. `Execute`'s `schema.Fields.ToDictionary(...)` then throws
     `ArgumentException: An item with the same key has already been added. Key: /Name`.
     Fix: `ExecuteMany` must **keep only the fields under the collection pointer** (both
     plan fields and schema fields) before relativizing, and drop the rest.
  2. **`ResolveJsonArray` cannot traverse arrays.** It splits the path on `.` and only
     ever calls `TryGetProperty`, so a `Root` such as
     `$.techSpecs.tables[*].specs[*]` resolves to nothing and yields zero items. The
     14 spec rows are nested one array inside another, so this must work. Extend it to
     handle `[*]` (flatten every element at that level) and `[n]` (index), consistent
     with the `JsonPath` locator grammar from T3.
  Add unit tests for both: a schema whose item field name collides with a top-level
  field name, and a doubly-nested `[*]` root. Depends on: T5.

- [x] **T9 — The detail extraction plan, as committed data.**
  Hand-write canonical plan JSON for `lenovo-com/tablet-detail` under
  `samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/`, with
  `Tier = StructuredData`. It pulls name/price/currency/availability/part-number/images
  from the Product `ld+json` block and the 14 spec rows from the `$pdpAllData` island,
  using only closed-vocabulary operations. Add a test asserting byte-identical
  `PlanSerializer` round-trip and `PlanValidator` success against the derived
  `TabletProduct` schema. Depends on: T5, T8.

- [x] **T10 — `detail --offline` works end to end.**  Implement `detail`: load the committed plan, derive the schema, read the fixture via
  T6's provider, run `Execute` + `ExecuteMany`, validate, materialize, print JSON to
  stdout, exit 0. On failure: stdout empty, diagnostic on stderr, non-zero exit. Save
  golden output to `samples/Sanare.Samples.Lenovo.State/golden/yoga-tab-gen2.json`. Add
  an integration test asserting exit 0, parseable stdout, name `Lenovo Yoga Tab Gen 2`,
  price `649.01`, currency `EUR`, and **>= 14** spec rows each with non-empty `Name` and
  `Value`. Depends on: T9.

- [x] **T11 — The lister plan and `list --offline`.**
  Hand-write the `lenovo-com/tablet-lister` plan, implement `list --offline` streaming
  listings as a JSON array deduplicated by `ProductUrl`, and save
  `samples/Sanare.Samples.Lenovo.State/golden/tablet-list.json`. Integration-test exit 0,
  a non-empty array, no duplicate `ProductUrl`, and absolute `ProductUrl` values.
  Depends on: T10.

- [x] **T12 — The `validate` command.**
  Implement `validate`: run `PlanValidator` over both committed plans against their
  derived schemas, report per-plan results on stdout as JSON, exit 0 only when both are
  valid. Integration-test the success path and a deliberately corrupted temp-copy plan
  producing a non-zero exit. Depends on: T11.

- [x] **T13 — Architecture guard test.**
  Add a test scanning every `.cs` file under `samples/Sanare.Samples.Lenovo/` that fails
  on CSS-selector-looking strings, XPath literals, JSON paths into Lenovo payloads, or
  any `lenovo.com` URL other than the two declared start URLs. This test is what proves
  the product claim. Depends on: T12.

- [x] **T14 — Sample README + documentation reconciliation.**
  Write `samples/Sanare.Samples.Lenovo/README.md` (commands, the offline guarantee,
  where plans live, how to refresh fixtures manually). Update `README.md`,
  `DEVELOPMENT.md`, and `docs/features/overview.md` so statuses match reality, including
  row 18. Claim nothing the code does not do. Depends on: T13.

- [ ] **T15 — Final verification sweep.**
  Re-run the whole definition of done from a clean build. Then prove the bars held by
  grepping for newly added `#pragma warning disable`, `NoWarn`, and `Skip =`, and for
  selector strings in sample C#. Confirm the non-Browser test count is **>= 611**. Write
  the actual numbers into Notes. Depends on: T14.

## Notes

Each iteration starts with a blank context; this section is the only memory between
iterations. Append short, factual findings — what you changed, what surprised you, what
the next iteration needs to know.

- A first run's two iterations both **timed out at the 20-minute budget** on a much
  larger version of T1 and produced no code. The plan has since been split into the
  smaller tasks above, and the survey results they spent their time rediscovering are
  recorded in "Known code constraints". Do not re-survey; start editing.

- T1 completed: `PlanExecutor.Locate` now partitions `Locators` into pipelines separated by document locators. Value-kind operations are passed the preceding string; repeated `SelectFirst`/`SelectAll`/`XPath` restart from the document as fallback candidates. `Text`/`Attribute` value handling parses the piped HTML string for the currently supported subset. Added `PlanExecutorTests` for piping and fallback restart. Verified with `dotnet test tests\\Sanare.Core.Tests\\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PlanExecutorTests" --no-restore` (2 passed). T2 can extend the two dispatch methods with its supported operations.

- T2 was already fully implemented across iterations 2–3 (commits `9d250c4`, `8c8fb55`) but the checklist box was never ticked — this iteration just verified and checked it off, no code changes needed. `HtmlDocument.SelectXPath` (AngleSharp.XPath `SelectSingleNode(...).TextContent`) and `HtmlDocument.OuterHtml` exist. `PlanExecutor.LocateFromDocument`/`LocateFromValue` support `XPath` (1 arg, document-restart semantics via `IsDocumentLocator`), `Html` (0 args, yields document markup as a first step or passes through the piped value when chained), and `RegexCapture` (1–2 args, `RegexOptions.NonBacktracking` + 1s timeout, catches `RegexParseException`/`RegexMatchTimeoutException`/`NotSupportedException` and returns null on miss rather than throwing). `PlanExecutorTests.cs` covers: XPath document location, Html→RegexCapture chaining, RegexCapture with an explicit group index, and a RegexCapture miss yielding null. Verified clean: `dotnet build Sanare.slnx -c Release` → 0 warnings/errors; `dotnet test Sanare.slnx -c Release --filter "Category!=Browser"` → 617 passed (Core 366, Http 181, Abstractions 41, Browser-tagged-but-non-Browser 29), 0 failed — comfortably above the 611 floor.

- T3 completed: `PlanExecutor` now evaluates a deliberately small, XML-documented JSONPath subset over piped JSON: property paths, non-negative array indices, and array wildcards (which select the first scalar result). Invalid paths, malformed JSON, and misses return null; chained JsonPath misses also add an `SNR-EXT-001` warning diagnostic. Tests cover nested properties, indexed and wildcard arrays, misses/invalid paths, and malformed JSON. Verified with `dotnet test tests\\Sanare.Core.Tests\\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PlanExecutorTests" --no-restore` (12 passed, 0 failed).

- T4 completed: `TryTransform` now supports Split (newline-delimited intermediate values), Index, Concat, Coalesce (first nonblank newline-delimited value), Exists, pairwise case-insensitive MapEnum, invariant/culture-aware ParseInt and ParseDecimal, and ParseBool. `Html` used as a transform exposes HTML-to-readable-text flattening through `HtmlToReadableText`; it inserts list/block boundaries before normalized text. Added unit coverage for every operation. Verified `dotnet test tests\\Sanare.Core.Tests\\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PlanExecutorTests" --no-restore` (22 passed) and `dotnet build Sanare.slnx -c Release --no-restore` (0 warnings/errors). Next: T5 is unblocked and is the riskiest remaining ready task.

- T5 completed: added `IPlanExecutor.ExecuteMany` and collection execution for CSS-rooted HTML elements and `$.property.path` JSON arrays. Collection field pointers are made relative by stripping the schema collection pointer plus `/*`; every selected item is executed, so missing required values remain failed outcomes. `Pagination.MaxItems` clamps selected items. JSON items are wrapped in a local script element so the existing locator pipeline can read them. `HtmlDocument` now retains raw content and can serialize all CSS matches. Added tests for HTML failures preserved, JSON extraction and clamp, and empty selections. Verified `dotnet test tests\\Sanare.Core.Tests\\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PlanExecutorTests" --no-restore` (25 passed, 0 failed). Next unblocked task: T6, T7, or T8 (T8 depends on T7); T6 is independent.

- T6 completed: added `FileFixtureContentProvider`, which eagerly reads a data-only JSON manifest (`entries`: `sourceId`, absolute `url`, relative `file`) and maps requests to files below its configured fixture root. Missing mappings and missing mapped files return `false`; path traversal is rejected. It performs file I/O only, so it opens no sockets. Added hit, unmapped miss, and missing-file tests. Verified `dotnet test tests\Sanare.Core.Tests\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~FileFixtureContentProviderTests" --no-restore` (3 passed, 0 failed). Next unblocked task: T7.

- T7 completed: created the `net10.0` console sample with usage written to stderr and exit 0, plus the Lenovo test project; both are registered in `/samples/` and `/tests/` respectively in `Sanare.slnx`. The sample references `Sanare.Core` and `Sanare.Abstractions`; the test project references the sample and uses the existing xUnit package versions. Verified `dotnet build Sanare.slnx -c Release` succeeds with 0 warnings and 0 errors. Next unblocked task: T8.

- T8 completed: added writable, parameterless `TabletListing`, `TabletProduct`, and `ProductSpecification` classes (rather than positional records) because `DocumentMaterializer` requires writable properties. `TabletProduct.Specifications` is the sole `[ScrapeCollection]`, producing `/Specifications/*` field pointers. Added source-generated camel-case `LenovoJsonContext`, so prices serialize as JSON numbers. Made `Program.Main(string[] args)` public to unit-test the current usage stdout/stderr contract. Added schema derivation, source-generated serialization, and stderr-only usage tests. Verified `dotnet test tests\\Sanare.Samples.Lenovo.Tests\\Sanare.Samples.Lenovo.Tests.csproj -c Release --no-restore` (3 passed) and `dotnet build Sanare.slnx -c Release --no-restore` (0 warnings/errors). Next unblocked task: T9.

- **Supervisor note (before T9 was attempted).** The supervising session ran the real `PlanExecutor` against the real committed fixture rather than reading the code, and found the two `ExecuteMany` defects now written up as **T8b**. The scalar `ld+json` locator chain in "Established ground truth" is verified working output, not a guess: it returned `Lenovo Yoga Tab Gen 2`. Do **T8b first** — T9 and T10 cannot pass without it.

- T8b completed: `ExecuteMany` now filters both plan and schema fields to the declared collection pointer before relativizing, preventing collection item names from colliding with top-level fields. JSON collection roots now traverse `[*]` and `[n]` selectors across nested arrays and return every selected item. Added collision and doubly nested wildcard regression tests. Verified `dotnet test tests\\Sanare.Core.Tests\\Sanare.Core.Tests.csproj -c Release --filter "FullyQualifiedName~PlanExecutorTests" --no-restore` (27 passed, 0 failed). Next unblocked task: T9.

- **Supervisor note after T8b.** T8b's fix is confirmed correct by execution against the real fixture: the nested root `\$.techSpecs.tables[*].specs[*]` now yields exactly **14** items and the pointer collision is gone. A third constraint surfaced while verifying: `JsonPath` cannot be a field's **first** locator, because the first step always runs against the document. Prefix it with `SelectFirst script.sanare-json-item` — the full verified recipe is now in "Established ground truth". No further executor changes are needed for T9; write the plan JSON to match that recipe.

- T13 completed: added `tests/Sanare.Samples.Lenovo.Tests/ArchitectureGuardTests.cs`, which enumerates every `.cs` file under `samples/Sanare.Samples.Lenovo/` (excluding `bin`/`obj`), extracts each C# string literal, and fails if any literal is, as a whole, CSS-selector-looking (leading `.`/`#` selector, a known-HTML-tag-qualified `tag.class`/`tag#id` compound, an attribute **value** selector, a pseudo-class/element call, or a spaced child/sibling combinator), XPath-looking (starts with `/`, `./`, or `../` **and** contains `@`, `[`, or `(` so plain RFC 6901 JSON pointers like `/Specifications/` are not false positives), or JSONPath-looking (`$.` / `$[` prefix). A separate test regex-scans for any `lenovo.com` URL and asserts it is one of exactly the two declared start URLs. Verified each detector actually fires by temporarily injecting `"div.product-name"`, `"$.offers.price"`, and `"https://www.lenovo.com/evil"` into `Program.cs`, confirming the corresponding test failed, then reverting (confirmed via `git diff` showing no residual change). While building this test I found and removed a real, if minor, violation of the "no Lenovo content logic in C#" bar: `Program.cs` had an unused, never-called `ExtractImages` method containing a hardcoded Lenovo JSON fragment literal (`"\"image\":[\"//"`) — dead code left over from an earlier iteration. Removed it; `Program.cs` already extracts images via the plan's locators, not this method. Verified `dotnet build Sanare.slnx -c Release` (0 warnings/errors) and `dotnet test Sanare.slnx -c Release --filter "Category!=Browser" --no-build` (Abstractions 41, Http 181, Browser-tagged-but-non-Browser 29, Samples.Lenovo 13, Core 390 — **654 total, 0 failed**, above the 611 floor). Next unblocked task: T14.

- T14 completed: added `samples/Sanare.Samples.Lenovo/README.md` (commands, the offline/zero-socket guarantee, where the two plans live and what they do, and a manual-fixture-refresh procedure — there is no live-capture mode in this sample by design). Reconciled docs with reality: `docs/features/overview.md` row 18 moved `draft` → `partial` with a rationale note explaining exactly what's implemented (`detail --offline`/`list --offline`/`validate`) versus deferred (`--live`/`capture`/`approve`, which depend on the still-partial/draft acquisition and authoring rows). Updated the root `README.md` status paragraph, added the sample project rows to its project-structure table, removed the now-stale "JSON/structured-data extraction unimplemented" bullet (T2/T3 implemented it), and qualified the pagination bullet to note the sample only implements single-page offline commands. Updated `DEVELOPMENT.md`'s pagination todo to reflect that `ExecuteMany` now exists (page-to-page enumeration still doesn't) and added a new `[x]` todo line documenting the sample's offline slice and its explicitly out-of-scope `--live`/`capture`/`approve` modes. Verified `dotnet build Sanare.slnx -c Release --no-restore` (0 warnings/errors) and re-ran `dotnet run --project samples/Sanare.Samples.Lenovo -- validate` to confirm the README's documented output (`isValid: true` for both plans) still matches reality. Next unblocked task: T15.

- T9 completed while repairing the failed validation: its `DetailPlanTests` test had been added before the required `samples/Sanare.Samples.Lenovo.State/scripts/plans/lenovo-com/tablet-detail.json` data file, so `File.ReadAllText` threw `FileNotFoundException`. Added the byte-canonical plan with `StructuredData` tier, Product `ld+json` scalar locator pipelines, the nested specification root, and canonical provenance. It validates against the derived `TabletProduct` schema. Verified `dotnet test tests\\Sanare.Samples.Lenovo.Tests\\Sanare.Samples.Lenovo.Tests.csproj -c Release --filter "FullyQualifiedName~DetailPlanTests" --no-restore` (1 passed). Next unblocked task: T10.

- T10 completed: `detail --offline` reads the canonical plan and the data-only fixture manifest, executes scalar and collection extraction, materializes `TabletProduct`, and writes only source-generated JSON to stdout. Added fixture manifest and committed golden output, plus an integration-style console redirection test (5 Lenovo tests pass). The executor now supplies the plan acquisition URL as the coercion base URI so protocol-relative product images coerce to HTTPS; it also recognizes the `$pdpAllData` JSON island for JSON collection roots. Decimal coercion uses invariant culture to preserve the plan's JSON `649.01` as a decimal (the PowerShell display renders it as `649,01` under the local culture). Verified `dotnet test tests\\Sanare.Samples.Lenovo.Tests\\Sanare.Samples.Lenovo.Tests.csproj -c Release --no-restore` (5 passed) and `dotnet build Sanare.slnx -c Release --no-restore` (0 warnings/errors). Next unblocked task: T11.

- T11 completed: added the data-only lister plan, fixture manifest entry, golden `tablet-list.json`, and `list --offline`; it emits a deduplicated JSON array with absolute product URLs. `ExecuteMany` now also accepts a top-level item schema and wraps HTML item markup in a parent so a root element can be selected during item extraction. The marketing fixture provides one link with visible `textvalue`, so the verified output has one listing. Added plan-validation and command integration tests. Verified `dotnet test tests\\Sanare.Samples.Lenovo.Tests\\Sanare.Samples.Lenovo.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ListCommandTests"` (2 passed) and `dotnet build Sanare.slnx -c Release --no-restore` (0 warnings/errors). Next unblocked task: T12.

- T12 completed: `validate` loads both committed plans, derives `TabletProduct`/`TabletListing` schemas, runs `PlanValidator`, and writes a JSON array of `{planSourceId, isValid, defects}` reports to stdout, exiting 0 only when every report is valid (non-zero otherwise, still with a parseable report on stdout — diagnostics belong in the report, not stderr, so CI can read why a plan failed). Added `PlanValidationReport` to `TabletSchemas.cs` and registered it (plus its array) in `LenovoJsonContext`. `RunValidate` takes an `internal` plans-directory override (new `InternalsVisibleTo("Sanare.Samples.Lenovo.Tests")` in `Program.cs`) so a test can point it at a temp directory holding one untouched plan copy and one deliberately corrupted copy (a field pointer renamed to something absent from the schema) without touching the committed plans. **Found and fixed a real test-isolation bug while adding this**: `ListCommandTests`, `TabletSchemasTests`, and the new `ValidateCommandTests` each redirect the process-wide `Console.Out`/`Console.Error`, but each test class declared its own private `Lock`, and xUnit runs different test classes in parallel by default — so two of these tests raced on live console state and failed nondeterministically. Fixed by extracting a single shared `ConsoleTestLock.Instance` used by all three files; do the same for any future command test that redirects console streams. Verified `dotnet test tests\\Sanare.Samples.Lenovo.Tests\\Sanare.Samples.Lenovo.Tests.csproj -c Release --no-build` (9 passed, 0 failed) and the full non-Browser sweep `dotnet test Sanare.slnx -c Release --filter "Category!=Browser" --no-build` (Abstractions 41, Http 181, Browser-tagged-but-non-Browser 29, Samples.Lenovo 9, Core 390 — 650 total, 0 failed, well above the 611 floor) and `dotnet build Sanare.slnx -c Release` (0 warnings/errors). Next unblocked task: T13.
