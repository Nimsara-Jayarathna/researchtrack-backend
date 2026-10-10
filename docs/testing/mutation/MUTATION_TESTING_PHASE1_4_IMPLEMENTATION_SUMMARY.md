# Mutation Testing — Phase 1–4 Implementation Summary

Implemented on top of the existing ResearchTrack unit/mock suite.

## Added

- repository-local `dotnet-stryker` 5.0.0 tool pin;
- focused Stryker configuration for Project business rules;
- focused Stryker configuration for Auth password policy;
- focused Stryker configuration for Submission file/responsibility/workflow rules;
- `scripts/mutation-baseline.sh` for repeatable local/CI mutation runs;
- `scripts/mutation-summary.py` for compact baseline evidence;
- manual `.github/workflows/backend-mutation.yml` workflow;
- full Phase 1–4 mutation-testing guide.

## Important design decision

Mutation testing reuses the existing xUnit projects. There is no duplicated `MutationTests` project. A surviving meaningful mutant is fixed by strengthening the corresponding normal unit test, so the unit-test suite and mutation quality work reinforce one another.

## Baseline policy

No mutation-score break threshold is enforced yet. The initial Stryker configs use `break: 0` intentionally. Run the baseline first, inspect survivors, then choose a final regression threshold from measured evidence.

## Validation note

The package contains configuration and script-level validation, but the current packaging environment does not provide the .NET SDK. The first actual mutation score must be produced on a .NET 10 developer machine or through the included manual GitHub Actions workflow.

## xUnit v3 / Stryker baseline correction

The mutation configs now exclude `Category=DatabaseIntegration` tests and set `coverage-analysis` to `off`. This avoids the failed coverage-capture optimisation observed on the first Project run while retaining the VSTest runner so the xUnit category filter continues to work. The previous `0.00%` Project result is not a valid baseline and must be rerun. The mutation runner also uses `python3` when available and Python cache files are ignored.

---

## Phase 1 dedicated-harness correction

The original xUnit v3 Stryker entry points are superseded by the dedicated
xUnit v2 mutation harness documented in
`MUTATION_TESTING_PHASE1_DEDICATED_HARNESS_SUMMARY.md`. Do not record the prior
`K 0` xUnit v3 runs as the mutation baseline.
