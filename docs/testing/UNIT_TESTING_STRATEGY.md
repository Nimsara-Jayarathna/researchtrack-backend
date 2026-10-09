# ResearchTrack Unit Testing Strategy

## Purpose

This test layer is designed for the SE3112 Unit Testing & Mocking workstream and deliberately prepares the backend for the next two stages: dependency mocking and mutation testing.

The implementation follows these engineering rules:

1. **Test observable business behaviour, not implementation trivia.**
2. **Prefer deterministic pure tests** for validation, state transitions, parsing, normalization, authorization decisions, and calculations.
3. **Use parameterized tests for boundaries and equivalence classes.**
4. **Make assertions mutation-resistant** by checking exact outputs, exact fields, exact status codes, and important state preservation.
5. **Do not call live infrastructure** from unit tests. No live Jira, GitHub, S3, MySQL, Azure, or HTTP dependency is required by the pure unit layer.
6. **Keep integration tests separate** and preserve the existing integration test suite.
7. **Do not introduce a mocking framework prematurely.** Service orchestration tests that require interaction verification are intentionally the next phase.
8. **Do not optimize for artificial 100% line coverage.** The priority is business-critical branch and rule coverage.

## Current architecture

The backend already uses service-specific xUnit projects and centrally managed versions for xUnit, Microsoft.NET.Test.Sdk, and coverlet.collector. This implementation extends those existing projects instead of creating a monolithic test project.

```text
tests/
├── ResearchTrack.AuthService.Tests
├── ResearchTrack.Gateway.Tests
├── ResearchTrack.GitHubService.Tests
├── ResearchTrack.JiraService.Tests
├── ResearchTrack.MeetingService.Tests
├── ResearchTrack.ProjectService.Tests
├── ResearchTrack.SubmissionService.Tests
└── ResearchTrack.Testing
```

## Phase 1 implemented in this revision

### Project Service

Added direct unit coverage for the two main pure business-rule components:

- `ProjectRequestValidator`
- `ProjectMilestonePolicy`

Covered behaviours include:

- required field normalization
- exact maximum-length boundaries
- supported semester validation
- student selection and duplicate prevention
- leader membership rule
- milestone required/date/chronology validation
- milestone status normalization
- open/terminal state rules
- completed milestone immutability
- previous/next milestone chronology on update
- project progress calculation
- cancelled milestone exclusion
- next-open-milestone calculation
- aggregate application to the project

`InternalsVisibleTo` is used for the ProjectService test assembly so internal business policies remain internal to production code while remaining directly testable.

### Auth Service

Expanded password-policy coverage with:

- exact minimum/maximum boundaries
- independent uppercase/lowercase/digit/special-character rules
- disabled-rule behaviour
- null/blank API guard clauses
- typed configuration mapping
- missing, placeholder, malformed, and out-of-range configuration values

### Submission Service

Expanded pure rule coverage for:

- extension normalization
- missing extension behaviour
- canonical MIME mapping
- alternate ZIP MIME handling
- mismatched/unknown MIME rejection
- deterministic allowed-type parsing/serialization
- safe file-name path stripping and maximum-length truncation
- responsibility mode normalization
- missing/invalid assigned student rules
- stale assignment response behaviour
- missing project leader behaviour
- authorization status distinctions (`403` vs `409`)

### Existing strong unit layers retained

The uploaded backend already contains extensive unit/component coverage for:

- Jira mapping, queries, workload, sprint progress, sync state, webhook validation and token protection
- GitHub repository URL parsing, app configuration, installation state/flow/repository handling, webhook verification/ingress, repository sync client, app clients and permission rules
- Meeting channels, records, contract validation and student authorization
- Gateway performance-rate-limit and trusted-proxy behaviour
- Submission workflow rules
- Auth registration/password rules plus integration tests

The new work fills the largest pure-rule gaps without duplicating those suites.

## Test naming and arrangement

Tests use explicit behaviour names such as:

```text
Method_Condition_ExpectedOutcome
```

Each test should follow Arrange / Act / Assert even when comments are omitted because the phases are obvious from the code.

Use `[Theory]` when multiple values exercise the same rule. Use `[Fact]` when the scenario has unique state or multiple assertions describing one behaviour.

## Mutation-testing readiness

The unit layer is intentionally mutation-friendly. Tests assert exact outcomes around common mutation operators:

- `<` vs `<=` at date and length boundaries
- boolean condition negation
- allowed/denied state transitions
- `null`/empty branch changes
- collection inclusion/exclusion
- arithmetic changes in progress percentage
- status-code changes
- string normalization and trimming
- duplicate detection removal
- terminal/open status set changes

When Stryker.NET is introduced, prioritize these production namespaces first:

```text
ResearchTrack.ProjectService.Features.Projects
ResearchTrack.AuthService.Features.Passwords
ResearchTrack.SubmissionService.Features
ResearchTrack.GitHubService.Features.Installation
ResearchTrack.GitHubService.Features.Webhooks
ResearchTrack.JiraService.Features
```

Avoid using generated EF migrations, startup wiring, DTO-only records, and trivial property containers as mutation-score targets.

## Phase 2: mocking and interaction verification

Do this after the pure unit suite is green.

Introduce exactly one mocking library centrally (Moq or NSubstitute) and use it only where a dependency interaction is part of the contract.

Priority services:

1. `ResearchSubmissionService`
2. `SubmissionRequirementService`
3. `ProjectService`
4. `SupervisorDashboardService`
5. `UserAuthenticationService`
6. `PasswordResetService`
7. `RegistrationService`
8. `GitHubRepositorySynchronizationService`
9. `GitHubWebhookEventProcessor`
10. `GitHubReconciliationService`
11. `JiraSyncService`
12. `JiraConnectionService`

Mock tests should verify both positive and negative interaction paths, especially `Once` and `Never` behaviour, dependency failures, idempotency, and preservation of previously valid state.

## Phase 3: coverage and CI

After mocking coverage is complete:

- run all test projects in CI
- collect coverlet output
- merge/report backend coverage
- publish coverage artifacts
- surface test counts and coverage in the GitHub Actions summary
- optionally introduce a quality threshold for business logic only
- keep integration and external-system tests distinct from fast unit tests

## Local verification commands

From the repository root:

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
dotnet test ResearchTrack.sln -c Release --no-build
```

Coverage collection can be performed per test project or solution-wide:

```bash
dotnet test ResearchTrack.sln \
  -c Release \
  --collect:"XPlat Code Coverage"
```

Do not declare a final coverage percentage until the generated coverage report has been inspected. Structural test presence is not the same thing as measured runtime coverage.
