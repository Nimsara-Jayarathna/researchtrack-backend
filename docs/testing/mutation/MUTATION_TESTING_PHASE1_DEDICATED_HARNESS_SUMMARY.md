# Mutation Testing Phase 1 — Dedicated Harness Summary

Implemented a dedicated Stryker-compatible xUnit v2 mutation harness while
preserving the existing xUnit v3 unit/mock suite.

## Added

- Project mutation harness for milestone policy + request validation.
- Auth mutation harness for password policy boundaries/rules.
- Submission mutation harness for file, responsibility and workflow rules.
- Stryker configurations colocated with the dedicated harnesses.
- `scripts/mutation-harness-verify.sh` controlled-defect verification.
- Updated `scripts/mutation-baseline.sh` to run only the dedicated harnesses.
- Manual mutation workflow now verifies the harness before running Stryker.
- `InternalsVisibleTo` entries only where internal business-rule classes require
  access from the mutation harness.

## Isolation

The dedicated mutation projects are intentionally excluded from the main
`ResearchTrack.sln`; therefore they do not duplicate the existing unit tests or alter
normal unit/mock coverage metrics.

## Required first command

```bash
bash ./scripts/mutation-harness-verify.sh
```

Only after that passes should the first real Stryker baseline be recorded.

## Tool manifest resolution hardening

The repository now has a single canonical local-tool manifest at `.config/dotnet-tools.json` containing EF, ReportGenerator, and Stryker. The competing root `dotnet-tools.json` was removed. The mutation runner explicitly restores this manifest and invokes Stryker through `dotnet tool run dotnet-stryker --`, preventing nested harness directories from resolving the wrong tool manifest.
