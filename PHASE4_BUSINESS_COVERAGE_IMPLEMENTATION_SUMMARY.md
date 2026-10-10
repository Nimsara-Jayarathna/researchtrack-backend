# SE3112 Phase 4 Implementation Summary

This branch now contains the risk-based coverage-hardening phase for Unit Testing & Mocking.

## Implemented

- Added targeted tests for previously weak/zero-coverage business services in Auth, Project, Submission, and GitHub.
- Added supervisor dashboard aggregate coverage.
- Preserved the existing 443-test Phase 1/2 suite and database-integration separation.
- Added repository-local ReportGenerator tooling.
- Added `scripts/coverage.sh` for repeatable test + Cobertura + merged HTML/Markdown/CSV/JSON reports.
- Added both an all-source report and a business-logic quality-gate report.
- Updated backend CI to run the unit/mock coverage workflow, append coverage to GitHub Actions Summary, and upload evidence artifacts.
- Added a coverage gap register for mutation-driven follow-up rather than random DTO/controller tests.

## Runtime verification required

Run on the .NET 10 development machine/CI runner:

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
./scripts/coverage.sh --no-build
```

Do not set a hard percentage gate until the new hardened baseline has been observed. If the business-scope baseline supports it, enable values such as `COVERAGE_MIN_BUSINESS_LINE=75` and `COVERAGE_MIN_BUSINESS_BRANCH=60` in CI.
