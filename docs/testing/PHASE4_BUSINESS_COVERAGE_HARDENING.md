# Phase 4 — Business-Critical Coverage Hardening

## Why this phase exists

The first merged baseline was 40.8% line and 31.7% branch coverage across broad production source. The percentage was useful, but the class-level report was more important: several core orchestration services had weak or zero unit coverage while DTOs, controllers, HTTP adapters, storage clients, and persistence plumbing also affected the aggregate.

This phase follows a risk-based approach instead of chasing a cosmetic global percentage.

## Added hardening tests

- **Auth** — `UserDirectoryService`: query boundaries, student-only search, registration-number search, duplicate ID resolution, supervisor exclusion, lookup/missing-user behavior.
- **Project** — `ProjectService`: ownership, supervisor/student access, accessible-project filtering, graph retrieval, valid/forbidden updates, leader validation, student removal/leader clearing.
- **Project dashboard** — empty dashboard, lifecycle aggregation, upcoming milestone window, member counts, recent-project limit, Jira projection placeholder.
- **Submission** — `SubmissionRequirementService`: create/normalize/persist interactions, invalid input side-effect prevention, archive immutability, close/reopen/archive transitions, deletion, not-found behavior.
- **GitHub** — `GitHubEvidenceQueryService`: paging validation, authorization-before-not-found behavior, PR status/search validation, empty page behavior for an enabled linked repository.

These tests supplement, rather than replace, the existing pure-rule and NSubstitute orchestration tests.

## Two coverage views

`./scripts/coverage.sh` now generates two reports:

1. **All-source coverage** — production assemblies with generated/bootstrap exclusions.
2. **Business quality-gate coverage** — `Features` classes plus Gateway policy code; excludes controllers, DTO-only contracts, persistence plumbing, infrastructure adapters, migrations, and application bootstrap.

This makes the CI gate academically defensible: unit-test coverage is measured against code unit tests are expected to own.

## Quality gate policy

No arbitrary percentage is hard-coded before the hardened suite is measured. After a successful baseline, thresholds can be enabled in CI using:

```bash
COVERAGE_MIN_BUSINESS_LINE=75 \
COVERAGE_MIN_BUSINESS_BRANCH=60 \
./scripts/coverage.sh --no-build
```

Adopt those values only if the measured baseline supports them without weakening tests or excluding legitimate business code.

## Verification

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
./scripts/coverage.sh --no-build
```

Expected evidence:

- zero unit/mock test failures;
- Cobertura coverage from every test project;
- `artifacts/coverage/coverage-summary.md`;
- full-source and business HTML reports;
- CI artifact `unit-mocking-coverage`.
