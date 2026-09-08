# Sanare

Sanare is a draft-first .NET library for schema-driven, self-healing product-data extraction. It uses the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/?pivots=programming-language-csharp) to author, version, execute, evaluate, and repair extraction plans while preferring efficient network and structured-data acquisition before escalating to Playwright.

## Documentation

- [Technical design](docs/sanare/tech-design.md) — canonical architecture, operational decisions, and delivery milestones.
- [Feature specifications](docs/features/overview.md) — ordered implementation index and component specifications.
- [Product idea](ideas/sanare/draft.md) — goals, reference scenarios, and MVP boundaries.

## Scope

The initial reference scenarios cover Lenovo tablet-list pagination and product/spec-table extraction. The design supports typed schema outputs, offline fixture-based validation, Git-backed plan history, source-level LLM budgets, adaptive request pacing and caching, and OpenTelemetry/Aspire-compatible observability.

This repository currently contains the product and technical specifications; implementation has not started.
