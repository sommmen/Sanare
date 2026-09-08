# Competitive Landscape — Sanare

> 2026-09-06 · Desk analysis of approaches available to a .NET product-data aggregator.

## Direct Competitors

### Commercial scraping / extraction APIs (Zyte, Bright Data, Apify, ScrapingBee, Firecrawl)

- **What they give**: managed proxy pools, browser farms, anti-bot handling, and increasingly an
  "AI extraction" endpoint that returns structured data for a supplied schema.
- **Gaps for this use case**:
  - Per-request pricing dominates at catalogue scale (thousands of product pages refreshed regularly).
  - The extraction logic is a black box — it cannot be unit-tested against your own fixtures, diffed,
    reviewed, or rolled back.
  - Data and target list leave your infrastructure.
  - No typed .NET contract; you re-map loosely-typed JSON on your side anyway.
  - Their anti-bot posture is aggressive (rotating residential proxies), which is more circumvention than
    this project wants.

### Scrapy + Python ecosystem (optionally with an LLM extraction middleware)

- **What it gives**: the most mature crawling framework — scheduling, throttling, middlewares, pipelines.
- **Gaps**: wrong runtime for a .NET backend (an extra service/process to operate); extraction is still
  hand-maintained; self-healing is a bolt-on you would have to build; no typed C# schema contract.

### Hand-written .NET scrapers (HttpClient + AngleSharp / HtmlAgilityPack, Playwright for the hard ones)

- **What it gives**: full control, minimum runtime cost, easy debugging, no LLM spend.
- **Gaps**: this *is* the status quo whose maintenance cost motivates the project — silent decay, human
  fix latency, live-site debugging traffic, no history of extraction logic.

## Indirect Competitors / Workarounds

### LLM-per-page extraction ("just paste the HTML into the model")

- **What it gives**: near-zero maintenance; adapts to layout changes automatically; trivial to start.
- **Gaps**: token cost scales linearly with pages crawled; latency per page is seconds not milliseconds;
  field-level output is non-deterministic run to run; large pages must be chunked/pruned; and there is no
  artifact to review, version, or bisect when quality drops.

### Agentic browser automation (browser-use, Playwright + agent loops, computer-use style agents)

- **What it gives**: can handle genuinely interactive flows a static parser cannot.
- **Gaps**: a browser per page is 100–1000× the resource cost of an HTTP fetch; non-deterministic; slow;
  and hugely over-powered for static spec tables that are already in the initial HTML.

### DIY on Semantic Kernel / AutoGen / LangChain-style frameworks

- **What it gives**: agent orchestration, tool calling, memory.
- **Gaps**: it is only the plumbing. The hard parts specific to this problem — tier policy, fixture corpus,
  script versioning, quality evaluation, heal dispatch, politeness — are entirely unbuilt.

### Vendor feeds / affiliate data feeds

- **What it gives**: clean structured data when they exist.
- **Gaps**: coverage is partial and shallow (rarely full spec tables), commercially gated, and simply
  absent for the sources in question — which is why scraping is on the table at all.

## Key Takeaway

Each competitor solves one axis: managed APIs solve blocking, LLM extraction solves adaptability, Scrapy
solves crawling, hand-written scrapers solve determinism and cost. **Nothing combines a typed .NET
library API, LLM authoring that yields a deterministic version-controlled artifact, a local fixture corpus
that keeps authoring/healing/regression off the live site, and an autonomous quality evaluator that
detects decay and dispatches repair.** That intersection is the defensible position for this project, and
it is also exactly what a product-data aggregator needs: cheap steady-state runs, fast recovery from
layout churn, and an auditable history of how each field was ever extracted.
