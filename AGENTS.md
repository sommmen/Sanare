# Repository guidance

## Overview

Sanare is a schema-driven, self-healing .NET scraping library. It uses the Microsoft Agent Framework to generate and repair versioned extraction plans, favoring normal HTTP and structured-data acquisition before browser automation. A v0.1 foundation is implemented (Abstractions, Core, Http, Browser, and the Lenovo sample); plan authoring/healing and pagination remain design-only. See [README.md](README.md) for the documentation index and current status, and [docs/sanare/tech-design.md](docs/sanare/tech-design.md) for the canonical design. Build and test commands are in [DEVELOPMENT.md](DEVELOPMENT.md).

## Related repositories

These are separate public .NET projects by the same author, usually cloned side by side; for cross-repo questions first check the sibling folders next to this repository's checkout. If they are not there, read them on GitHub.

- **Sanare** (this repo) — schema-driven, self-healing .NET scraping library built on the Microsoft Agent Framework. Independent of the other projects. <https://github.com/sommmen/Sanare>
- **dotnet-agent-surface** — library that exposes an application's operations through an MCP server, a CLI and generated agent skills from one catalog. Published as the `DotNetAgentSurface.*` NuGet packages and consumed by `Anvilboard` and `dotnet-efcore-mcp`. <https://github.com/sommmen/dotnet-agent-surface>
- **Anvilboard** — local-first, single-process issue tracker and dashboard backed by one SQLite file. <https://github.com/sommmen/Anvilboard>
- **dotnet-efcore-mcp** — MCP server that queries an existing EF Core application's database by reflecting over its `DbContext`. <https://github.com/sommmen/dotnet-efcore-mcp>
- **lightr** — C# API client for the Lightr handwritten-card SAAS, generated from its OpenAPI document. <https://github.com/sommmen/lightr>

## Project structure

- `src/` — library projects: `Sanare.Abstractions` (contracts), `Sanare.Core` (engine and local infrastructure), `Sanare.Http` (governed HTTP acquisition), `Sanare.Browser` (Playwright browser tier).
- `tests/` — one test project per `src/` project plus `Sanare.Samples.Lenovo.Tests`.
- `samples/` — `Sanare.Samples.Lenovo` console sample and its `.State` fixtures, golden output, and plan scripts.
- `docs/sanare/` — canonical technical design.
- `docs/features/` — implementation-ordered component feature specifications.
- `ideas/sanare/` — graduated product idea, discovery research, and trace state.
## Commit conventions

Use [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) for
every commit message and pull request title:

```
<type>[optional scope]: <description>

[optional body]

[optional footer(s)]
```

- `type` — one of `feat`, `fix`, `chore`, `docs`, `refactor`, `test`, `build`,
  `ci`, `perf`, `style`
- `scope` — optional; the module, package, or area the change touches
- `description` — a short, imperative summary of the change

Examples:

- `feat(auth): add refresh token rotation`
- `fix(api): handle null response from upstream service`
- `chore(deps): bump dependency versions`

<!-- comment-policy:begin - managed by the stallions comment-policy skill, edit it there -->

## Code comments

Default to writing no comment. A comment has to earn its place; most of the
time its absence is the correct outcome. Well-named identifiers and small
functions already carry the *what*, so a comment is only worth its maintenance
cost when it carries a *why* that the code genuinely cannot.

Write a comment only when the reason behind the code is non-obvious and a
competent reader could otherwise break it:

- A hidden external constraint, such as an API that rejects a correctly-cased
  header.
- A subtle ordering requirement or invariant that nothing in the signature
  implies.
- A deliberate workaround for a specific bug, platform quirk, or spec defect.
  Name the thing being worked around.
- Behaviour that would read as a mistake to someone who does not know the
  constraint.

Keep it to one line, two at the absolute most. Never a paragraph, never a
multi-line banner.

Do not write:

- Restatements of what the code already says (`// increment the counter`).
- Change narration or history (`// added for bug 123`, `// was previously
  Foo`). That belongs in the commit message and rots in the source.
- Section headers inside a function body (`// --- validation ---`). A function
  that needs signposting needs splitting.
- Restatements of a type annotation or signature.
- `TODO` or `FIXME` markers with no tracked issue and no owner. Fix it or file
  it.
- Commented-out code. Delete it; version control remembers.

Doc comments on public API surfaces (XML docs, JSDoc, docstrings) are a
separate concern, and this rule does not suppress them where the language or
project convention expects them. The same discipline applies to their bodies.

When editing existing code, redundant comments adjacent to the change are fair
game to delete. Do not turn that into a comment-stripping sweep of files the
change does not touch.

An explicit request for more comments overrides all of the above.

<!-- comment-policy:end -->
