# Auth Registration Mutation — Final Hardening Pass

## Why this pass exists

The first broad Auth survivor-hardening run improved most Auth areas substantially, but Registration remained the clear outlier:

| Auth scope | Hardened score |
|---|---:|
| Password policy | 100.00% |
| Authentication | 91.23% |
| Password reset | 76.70% |
| Registration | 41.69% |
| User account | 75.68% |
| User directory | 84.62% |

Registration therefore receives a focused final pass before Auth is frozen and mutation work moves to the next microservice.

## What this pass strengthens

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

Fast final Registration verification:

```bash
dotnet test tests/ResearchTrack.AuthService.Tests/ResearchTrack.AuthService.Tests.csproj \
  -c Release \
  --filter "Category!=DatabaseIntegration"

dotnet test tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj \
  -c Release

bash ./scripts/mutation-baseline.sh auth-registration
```

The `auth-registration` target verifies both test suites first and then reruns only the RegistrationService Stryker scope, avoiding a full six-scope Auth rerun during survivor iteration.

When Registration is stable, run the final full Auth evidence pass:

```bash
bash ./scripts/mutation-baseline.sh auth
```

## Final-score rule

Do not force Registration to 100% by broad mutation exclusions. Review remaining survivors and distinguish:

1. meaningful business/security behavior — strengthen the test;
2. equivalent mutation — document it;
3. non-contractual diagnostic/logging prose — document it rather than coupling tests to wording.

The final Auth regression threshold should be selected only after the final full Auth rerun and should use the weighted killed/survived aggregate generated in `artifacts/mutation/mutation-summary.md`.
