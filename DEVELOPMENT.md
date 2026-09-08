# Development

Work yet to be done in this repository. Keep this list current as work is
planned, started, and finished.

## Running locally

```powershell
dotnet build Sanare.slnx
dotnet test Sanare.slnx
dotnet format Sanare.slnx --verify-no-changes
```

Requires .NET SDK 9.0+. See [README.md](README.md) for the current architecture
and scope of the v0.1 foundation.

## Todo

- [x] Implement the v0.1 project skeleton, core domain/engine contracts, and a minimal deterministic offline engine path (`Sanare.Abstractions` + `Sanare.Core`), proving the intended architecture end-to-end via fixture-backed execution.
- [ ] Implement persistent/Git-backed extraction-plan storage and resolution (`plan-resolver`/`script-repository`), replacing `InMemoryExtractionPlanProvider`.
- [ ] Implement `IPlanValidator` and the full `extraction-plan-model` feature (JSON plan serializer, canonical writer, content hash, version upgrades) once plans can originate from outside the process.
- [ ] Implement live HTTP acquisition, then browser-tier (Playwright) acquisition, per `docs/sanare/tech-design.md`.
- [ ] Implement the pagination engine and remove the `NotSupportedException` from `IScrapeRunner.StreamAsync`.
- [ ] Add formal test cases for the M2–M7 milestones, including fixture regressions and self-healing negative paths.
- [ ] Define the exact Microsoft Agent Framework and OmniRoute integration packages, configuration, and failure behavior before implementing plan authoring/healing.
