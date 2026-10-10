# Phase 5 — Hotspot Coverage Hardening

This phase extends the verified unit/mocking suite using the Phase 4 coverage report as the input rather than adding low-value DTO/controller tests.

## Focus

- Project orchestration: update, ownership, leader management, members, milestones and failure paths.
- Submission orchestration: upload-session preconditions, completion authorization/state handling, review validation and download authorization.
- Auth failure/validation paths: password reset token/email validation and account password-change validation.
- GitHub evidence queries: real paged commit/contributor projections over an isolated in-memory store.

## Engineering principles

- Production behavior is not weakened to make tests pass.
- External collaborators are substituted; domain entities and EF projections remain real.
- Tests assert side-effect prevention on invalid requests and authorization failures.
- Boundary and branch tests are intentionally mutation-friendly.
- DatabaseIntegration remains a separate category and is not required for the fast unit/mocking quality gate.

## Verification

Run:

```bash
./scripts/coverage.sh
```

Do not set the final CI coverage threshold until this branch's new measured business-scope result is available. The next threshold should be based on the achieved baseline, with a small regression margin rather than an arbitrary number.
