# AuthService mutation hardening

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

These records preserve successive hardening passes. Scores below are historical measurements that motivated each pass, not newly verified results. Use the shared runbook for current service commands and the baseline document for scoring and gate status.

## Initial survivor hardening

This phase strengthens the real AuthService unit-test suite using evidence from the first full Auth mutation baseline. The same behavioral tests are mirrored into the dedicated xUnit v2 mutation harness so Stryker can evaluate them reliably without changing the normal xUnit v3 CI suite.

### Baseline that motivated this phase

| Auth scope | Baseline score | Killed | Survived |
|---|---:|---:|---:|
| Password policy | 100.00% | 38 | 0 |
| Authentication | 19.30% | 11 | 46 |
| Password reset | 67.96% | 70 | 33 |
| Registration | 43.39% | 128 | 167 |
| User account | 72.97% | 27 | 10 |
| User directory | 71.79% | 28 | 11 |

Weighted selected Auth baseline: 302 killed / 569 tested = 53.08%.

### Hardening added

`AuthMutationSurvivorHardeningTests.cs` is present in both the normal Auth test project and the dedicated Auth mutation harness. It targets survivor patterns that change security or business behavior rather than trying to kill every string/logging mutant.

The added tests cover refresh-token rejection and rotation, logout isolation, authenticated-user subject and role mapping, password-reset token state and cross-user isolation, newest-valid OTP selection, used/expired OTP rejection, registration-session lifecycle, duplicate student registration numbers, cleanup semantics, password-change refresh-token isolation, and student-directory search/ordering behavior.

### Intentionally not changed

Production AuthService behavior was not altered merely to improve mutation score. Mutants that are unreachable because of persistence invariants, semantically equivalent, or limited to non-contractual diagnostic/error prose should be reviewed and documented rather than hidden by broad exclusions.

## Registration follow-up

### Why this pass exists

The first broad Auth survivor-hardening run improved most Auth areas substantially, but Registration remained the clear outlier:

| Auth scope | Hardened score |
|---|---:|
| Password policy | 100.00% |
| Authentication | 91.23% |
| Password reset | 76.70% |
| Registration | 41.69% |
| User account | 75.68% |
| User directory | 84.62% |

This intermediate result motivated a subsequent registration-hardening pass.

### What this pass strengthens

The normal xUnit unit/mock suite and the dedicated Stryker harness now both exercise additional RegistrationService decisions that are especially mutation-sensitive:

- invalid student-email identifier rejected before persistence or email delivery;
- incorrect OTP hash does not consume the OTP or create a registration session;
- unknown-domain verified OTP produces an unresolved registration session requiring explicit role selection;
- missing/invalid requested role is rejected for unresolved sessions;
- server-resolved session role overrides contradictory client role input;
- successful student registration persists the normalized registration number, hashes the password, creates a refresh session, consumes the registration session, and passes the final role to access-token generation;
- raw registration tokens are accepted both with and without the API prefix;
- overlong registration-number input is rejected before database access;
- direct student registration enforces registration-number/email identity matching;
- direct supervisor registration never persists a student registration number;
- missing direct-registration fields are all surfaced as validation errors before database access.

These tests are intentionally behavior-oriented. They target boolean/equality, branch, statement-removal, role, token-state, and persistence-side-effect mutants rather than asserting every diagnostic string solely to inflate the score.

## Verification

Run `bash ./scripts/mutation-baseline.sh auth` from the repository root. This verifies both test suites and executes every finalized AuthService subscope. Follow the [runbook](../../runbook.md) for reporting and survivor review.
