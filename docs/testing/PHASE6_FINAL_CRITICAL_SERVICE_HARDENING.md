# Phase 6 — Final Critical-Service Coverage Hardening

Phase 6 is the final broad unit/mocking expansion before mutation testing. It was driven by the verified Phase 5 business-scope baseline of **48.4% line / 39.3% branch coverage** and targets high-risk orchestration code rather than DTOs or controllers.

## Added critical-path coverage

### GitHub repository synchronization

`GitHubRepositorySynchronizationServiceCoverageTests` covers missing links, unavailable/disconnected repositories, missing installation state, suspended installations, repository identity drift, successful empty synchronization, and a rich synchronization snapshot that persists branch, commit, contributor, pull-request, review, and sync-run state.

The tests use SQLite in-memory rather than EF InMemory because the production service relies on transactions and `ExecuteUpdateAsync` semantics.

### GitHub webhook event processing

`GitHubWebhookEventProcessorCoverageTests` covers ping and unsupported events, malformed payloads, missing installation metadata, default/non-default branch pushes, pull-request events, repository deletion, installation suspension/deletion/creation, and installation-repository removal handling.

### GitHub dashboard aggregation

`GitHubDashboardQueryServiceCoverageTests` covers empty projects, selected-repository validation, rich dashboard aggregation, multiple-repository selection, paging validation, activity paging/avatar projection, contributor ordering, and project-id validation.

### Auth registration

`RegistrationServiceCoverageHardeningTests` covers direct supervisor/student registration, server-assigned role enforcement, duplicate account handling, validation short-circuiting, OTP initiation, OTP verification, completion with refresh/access token creation, and non-critical success-email failure handling.

### Auth password reset and account password changes

`PasswordResetAndAccountCoverageTests` covers non-enumerating forgot-password behavior, token replacement, email delivery failure invalidation, token validation, successful reset with refresh-session revocation, password-reuse rejection, invalid tokens, missing accounts, incorrect current passwords, and successful password change.

### Submission lifecycle success paths

`ResearchSubmissionSuccessfulFlowCoverageTests` complements the earlier failure-path tests with successful upload-session creation/completion, completed-session idempotency, expired/missing-object handling, approve/request-changes reviews, revision session creation, download grants, and list/get mapping.

## CI regression floor

The CI coverage step now enforces a conservative regression floor based on the last verified baseline rather than an invented target:

- Business line coverage: **>= 45%**
- Business branch coverage: **>= 35%**

The floor is intentionally below the verified Phase 5 baseline (48.4% / 39.3%) so CI catches material regressions while leaving small reporting/tooling headroom. After Phase 6 is run on the real .NET runner, the floor can be raised to the new verified baseline minus sensible headroom.

## Why SQLite appears in selected unit-test projects

EF Core's InMemory provider is not a relational database and does not faithfully support relational transactions or `ExecuteUpdateAsync` behavior used by several orchestration services. SQLite in-memory is used only in tests that need those semantics; pure rule tests and simple query tests continue using EF InMemory where appropriate.

## Verification

Run from the repository root:

```bash
dotnet restore ResearchTrack.sln
./scripts/coverage.sh
```

A successful run must show a zero-warning/zero-error Release build, all `Category!=DatabaseIntegration` tests passing, and generated coverage evidence under `artifacts/coverage/`.
