# Stryker.NET xUnit v3 mutation-run fix

## Why the first Project mutation run reported 0 killed / 196 survived

The initial Stryker 5.0.0 run successfully discovered the Project test project and excluded database integration tests after the `Category!=DatabaseIntegration` filter was added. However, Stryker then logged that coverage capture failed while using its default coverage-based optimisation. The resulting `0 killed / 196 survived` result is therefore not accepted as the project mutation baseline.

## Final configuration used for the baseline

Each Phase 1–4 Stryker configuration now contains:

```json
"test-case-filter": "Category!=DatabaseIntegration",
"coverage-analysis": "off"
```

`test-case-filter` keeps the mutation run on the unit/mock suite only, so no test database connection is required.

`coverage-analysis: off` disables Stryker's coverage-based test-selection optimisation and runs the selected unit/mock tests against each mutant. This is intentionally slower but avoids accepting suspicious survivor results after coverage capture failed.

The runner remains Stryker's default VSTest runner. We intentionally do **not** switch to the MTP runner in this configuration because current xUnit v3 MTP runners do not expose the `--filter` option required by Stryker's `test-case-filter`; keeping VSTest preserves the `Category!=DatabaseIntegration` exclusion.

## Summary-script portability

`scripts/mutation-baseline.sh` now prefers `python3` and falls back to `python`. This fixes macOS systems where Python 3 is installed only as `python3`. Generated `__pycache__` and `.pyc` files are ignored by Git.

## Run

```bash
bash ./scripts/mutation-baseline.sh project
```

Only a run where mutants are meaningfully killed/survive after this fix should be recorded as the Project mutation baseline.
