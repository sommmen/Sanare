# Repository guidance

## Overview

Sanare is a specification-first project for a schema-driven, self-healing .NET scraping library. The intended implementation uses the Microsoft Agent Framework to generate and repair versioned extraction plans, favoring normal HTTP and structured-data acquisition before browser automation. See [README.md](README.md) for the documentation index and [docs/sanare/tech-design.md](docs/sanare/tech-design.md) for the canonical design.

## Project structure

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
