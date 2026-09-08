# Sanare (.NET / Microsoft Agent Framework)

> Status: Ready | Draft v1 | 2026-09-06

## One-Liner

A typed .NET library that turns "give me this URL plus this C# schema" into a versioned, LLM-authored,
git-tracked scraping script that runs against cheap HTTP first, escalates to Playwright only when needed,
and repairs itself automatically when the target site's layout changes.

## Problem

Product-data aggregation depends on dozens of sites that have no public API (Lenovo's product catalogue,
bol.com listings, retailer spec tables). Today each of these needs a hand-written scraper, and the cost is
not in writing it — it is in **keeping it alive**:

- **Silent decay.** A CSS class rename or a moved spec table does not throw; it returns `null`. The
  aggregation pipeline keeps running and quietly ingests incomplete products. Nobody notices until a
  downstream consumer complains that half the tablets have no battery capacity.
- **Fix latency.** Every break is a human ticket: reproduce, open devtools, find the new selector, patch,
  test, deploy. Days of latency per site, multiplied by the number of sites.
- **Expensive debugging loop.** Reproducing a break means hitting the live site again and again, which is
  both slow and the fastest way to get rate-limited or IP-banned mid-investigation.
- **Blocking.** Naive .NET `HttpClient` traffic is trivially fingerprintable (TLS/JA3 + header order +
  no `Sec-Fetch-*`), so many targets serve a challenge page or a cookie wall instead of content — and the
  scraper cheerfully parses the challenge page into empty output.
- **Playwright everywhere.** The common reaction is "just run a headless browser for everything", which is
  100–1000× the CPU/RAM per page and dramatically slows a catalogue crawl that mostly needs plain HTML.
- **No history.** When a scraper is regenerated or hand-patched there is no diff, no blame, no rollback —
  so a regression cannot be bisected, and a working extraction that got clobbered cannot be restored.

## Target Users

Concrete and narrow — not "everyone who scrapes":

1. **The product-data aggregation backend (primary).** A .NET service that needs `IReadOnlyList<TabletListing>`
   and `TabletSpecs` objects for a catalogue, on a schedule, from sites without APIs. It is the direct
   library consumer: it calls `IScrapeRunner.RunAsync<TSchema>(request, ct)` and expects typed results.
2. **The platform/data engineer who owns that backend.** Adds a new source by writing a POCO schema and a
   URL, reviews what the agent generated, approves promotions, and inspects the git history when output
   quality drops.
3. **The on-call engineer.** Wants an alert that says "lenovo-tablet-specs: `BatteryWh` null-rate went
   0.02 → 0.91, self-heal attempted, PR-equivalent commit `a4f21c` on branch `heal/2026-09-06`, awaiting
   approval" instead of "the nightly import looks weird".

Non-users (explicit): people who want a hosted SaaS scraping API, people scraping sites that offer a
usable API (use the API), and anyone wanting a general-purpose web agent.

## Core Concept

```
Consumer                 Framework                              Target site
--------                 ---------                              -----------
RunAsync<TSchema>(url) ──► 1. Look up a compiled, versioned script for (site, schema, version)
                           2. Cache hit?  → return typed result
                           3. No script?  → LLM authoring workflow:
                                a. Fetch page politely, persist to the local fixture corpus
                                b. Agent inspects the *fixture* (never the live site) and writes
                                   a strategy: JSON-LD / embedded JSON / internal API / CSS+XPath
                                   / (last resort) Playwright script
                                c. Compile + run against fixtures, score against the schema
                                d. Iterate until the score passes the gate
                                e. Commit the script to the on-disk git repo, tagged with the
                                   fixture hash it was validated against
                           4. Execute the script through the fetch pipeline
                              (polite rate limits, caching, ChatGPT-style browsing identity)
                           5. Validate output against TSchema → typed object + a quality report
                           6. Periodic evaluator watches null-rates/type failures across runs and
                              dispatches a self-heal run (re-capture fixture → diff → LLM repair →
                              validate against BOTH old and new fixtures → commit on a heal branch)
```

Five design commitments that distinguish this from "an LLM that scrapes":

1. **The LLM writes the scraper; it does not do the scraping.** Every run of a healthy site is
   deterministic, cheap, and token-free. LLM cost is paid at authoring and healing time only.
2. **Fixtures are the test suite.** Real captured pages (`tablet-lister.html`, `yoga-tab-gen2-specs.html`,
   `lister-page-2.json`, …) live on disk. Authoring, healing, regression testing, and CI all run against
   fixtures — the live site is touched only to capture or refresh a fixture.
3. **Scripts are git objects.** The library ships/embeds a real git repository on disk, so versioning,
   diffing, blame, branching for heals, and rollback come for free instead of being re-implemented as a
   bespoke "script_versions" table.
4. **Cheapest viable extraction tier wins.** Structured data (JSON-LD / `__NEXT_DATA__` / internal JSON
   endpoints) → HTML parsing → headless browser. Playwright is an escalation, not a default.
5. **Blend in rather than fight.** Present a plausible, consistent, *honest-about-being-a-bot-when-asked*
   browsing identity (modern browser header set, HTTP/2 ordering, `Sec-Fetch-*`, per-host session
   cookies, human-ish pacing), obey rate limits, cache aggressively, back off on 429/403. Bare minimum
   evasion — no CAPTCHA solving, no residential proxy rotation, no auth-wall bypass.

## Existing Solutions & Gaps

| Approach                                                          | What it gives                        | Why it is insufficient here                                                                                                                                              |
| ----------------------------------------------------------------- | ------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Hand-written scrapers (AngleSharp/HtmlAgilityPack + `HttpClient`) | Full control, fast, cheap            | All maintenance is human; silent decay; no healing; no typed contract per site                                                                                           |
| Scrapy / Python ecosystem                                         | Mature crawling, huge ecosystem      | Wrong language for a .NET aggregation backend; still hand-maintained; no self-healing                                                                                    |
| Commercial scraping APIs (Zyte/Bright Data/Apify/ScrapingBee)     | Managed proxies + some AI extraction | Per-request cost at catalogue scale; data leaves the building; extraction logic is opaque and un-versionable; you cannot unit-test their extractor against your fixtures |
| "LLM reads the page" (LLM-per-page extraction)                    | Zero maintenance, trivially adapts   | Token cost scales with pages (catalogue-scale = thousands/day); non-deterministic field-to-field; slow; no diffable artifact                                             |
| Browser-use / agentic browser automation                          | Very flexible                        | Browser-per-page cost; non-deterministic; not a typed library API; overkill for static spec tables                                                                       |
| Semantic Kernel / AutoGen DIY                                     | Agent plumbing                       | Just the plumbing — no scraping tier policy, no fixture corpus, no versioning, no heal loop                                                                              |

**The gap:** nothing in the .NET space combines (a) a *typed* schema-first library API, (b) LLM authoring
that produces a **deterministic, reviewable, version-controlled artifact** instead of a per-page LLM call,
(c) a **local fixture corpus** so authoring/healing/regression never hammers the live site, and
(d) an **autonomous quality evaluator** that notices decay and dispatches a repair.

## MVP Scope

**In:**

- `IScrapeRunner.RunAsync<TSchema>(ScrapeRequest, CancellationToken)` returning `ScrapeResult<TSchema>`
  (typed payload + provenance + quality report), plus `IAsyncEnumerable<T>` for paginated sources.
- Schema definition from plain C# POCOs (JSON-schema generated from the type, with attributes for
  descriptions/required/units/hints).
- Authoring workflow on the Microsoft Agent Framework: fixture capture → strategy selection → script
  generation → compile → fixture-validated scoring → git commit.
- Extraction tiers: structured-data (JSON-LD, embedded JSON blobs, internal JSON endpoints) → HTML
  (CSS/XPath) → Playwright, with automatic escalation and a recorded rationale for the tier chosen.
- Local fixture corpus on disk with a manifest (URL, captured-at, content hash, tier, redaction state).
- On-disk git repository for generated scripts: commit per authoring/heal run, branch per heal, tags for
  approved versions, rollback to any previous commit.
- Fetch pipeline: per-host rate limiting + jitter, `robots.txt` awareness, conditional requests / HTTP
  cache, disk response cache, exponential backoff on 429/403/5xx, ChatGPT-style browsing identity,
  cookie-wall and consent-banner handling.
- Quality evaluator: per-field null-rate / type-failure / drift tracking across runs, thresholds, and
  automatic dispatch of a self-heal run; heals require validation against the *previous* fixtures too, so
  a heal cannot silently regress fields it wasn't asked to fix.
- Pagination support: next-link, page-parameter, cursor, and infinite-scroll strategies with a global cap.
- Sample app covering the two reference targets:
  - `https://www.lenovo.com/nl/nl/tablets/` → full tablet product list (paginated).
  - `https://www.lenovo.com/nl/nl/p/tablets/android-tablets/yoga-tab-series/lenovo-yoga-tab-gen-2/len103y0003#tech_specs`
    → product info + the complete spec table.

**Out (deliberately):**

- CAPTCHA solving, proxy-rotation networks, TLS/JA3 spoofing beyond ordinary header/HTTP-2 realism,
  login/paywall bypass.
- A hosted service, UI, or multi-tenant control plane (library + sample app only).
- Distributed crawling/queue infrastructure (single-process, embeddable; the host schedules).
- Generic "ask the web anything" agent behaviour.

## Demand Validation Status

- [x] Problem backed by evidence — the aggregation system already needs Lenovo + bol.com data and neither
      exposes a usable public product API; the maintenance-decay pattern is the stated pain.
- [x] Target users identified and reachable — the aggregation backend and its owning engineer; the
      library's first consumer already exists.
- [x] Existing solutions analyzed — see the table above and `research/competitors.md`.
- [x] "What if we don't build this?" answered — see below.
- [x] At least one form of demand evidence — the concrete two-URL Lenovo requirement, plus bol.com named
      as the next source, is a real, dated, in-hand requirement rather than a hypothetical.

### What if we don't build this?

The aggregation system still gets built, but with N hand-written scrapers. The predictable outcome:
each new source costs days of engineering; breakages are discovered by data consumers rather than by the
system; investigation traffic against live sites gets the crawler throttled or blocked at exactly the
moment engineers need to iterate; and there is no artifact history, so "it worked last month" cannot be
recovered. The failure mode is not "no data" — it is **quietly wrong data**, which is worse for a product
aggregator than an outage. That is a significant enough consequence for the idea to graduate.

## Open Questions

Carried into the tech design as explicit decisions (not blockers):

- **Script representation**: compiled C# (Roslyn) vs. a declarative extraction DSL vs. both. Trade-off is
  expressiveness/typing against sandboxing risk and review reviewability.
- **Git integration**: LibGit2Sharp in-process vs. shipping/ shelling out to a `git` binary.
- **Sandboxing**: how far to isolate generated code (AssemblyLoadContext + separate process + resource caps).
- **Heal approval**: fully autonomous promotion vs. human-in-the-loop approval gate (Agent Framework
  supports both; likely configurable per source with autonomous as opt-in).
- **Fixture retention**: how many historical fixtures per source, and redaction of any incidental PII.

## Research Backlog

- Verify current Microsoft Agent Framework C# package IDs, workflow/checkpointing API, and structured-output
  API names against the live docs (in progress — feeds the tech design).
- Confirm the shape of Lenovo's NL catalogue pages: JSON-LD presence, any internal listing JSON endpoint,
  pagination mechanism, consent-wall behaviour.
- Confirm bol.com's terms/robots posture before adding it as a second reference source.

## Session History

- [2026-09-06] explore-initial-spark — Problem framing, target users, five design commitments, competitive
  gap analysis, MVP scope, and "what if we don't build this" answered. Idea marked **ready**; graduating to
  decompose → tech-design.
