# AuthService mutation survivor hardening

This phase strengthens the real AuthService unit-test suite using evidence from the first full Auth mutation baseline. The same behavioral tests are mirrored into the dedicated xUnit v2 mutation harness so Stryker can evaluate them reliably without changing the normal xUnit v3 CI suite.

## Baseline that motivated this phase

| Auth scope | Baseline score | Killed | Survived |
|---|---:|---:|---:|
| Password policy | 100.00% | 38 | 0 |
| Authentication | 19.30% | 11 | 46 |
| Password reset | 67.96% | 70 | 33 |
| Registration | 43.39% | 128 | 167 |
| User account | 72.97% | 27 | 10 |
| User directory | 71.79% | 28 | 11 |

Weighted selected Auth baseline: 302 killed / 569 tested = 53.08%.

## Hardening added

`AuthMutationSurvivorHardeningTests.cs` is present in both the normal Auth test project and the dedicated Auth mutation harness. It targets survivor patterns that change security or business behavior rather than trying to kill every string/logging mutant.

The added tests cover refresh-token rejection and rotation, logout isolation, authenticated-user subject and role mapping, password-reset token state and cross-user isolation, newest-valid OTP selection, used/expired OTP rejection, registration-session lifecycle, duplicate student registration numbers, cleanup semantics, password-change refresh-token isolation, and student-directory search/ordering behavior.

## Intentionally not changed

Production AuthService behavior was not altered merely to improve mutation score. Mutants that are unreachable because of persistence invariants, semantically equivalent, or limited to non-contractual diagnostic/error prose should be reviewed and documented rather than hidden by broad exclusions.

## Verification sequence

Run the normal unit/mock suite first, then coverage, then the Auth mutation suite:

```bash
dotnet test ResearchTrack.sln -c Release --filter "Category!=DatabaseIntegration"
./scripts/coverage.sh --no-build
bash ./scripts/mutation-baseline.sh auth
```

Compare the new per-subscope killed/survived counts with the baseline above. Only after the hardened score is measured should a mutation regression threshold be selected.
