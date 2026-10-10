# SE3112 Unit Testing & Mocking — Coverage and CI Evidence

## Purpose

This phase turns the verified unit/mock suite into repeatable evidence. It does **not** mix database integration tests into the fast unit gate.

The current verified local unit/mock baseline is 443 tests across Gateway, Auth, Project, GitHub, Jira, Meeting and Submission with 0 failures and 0 skips.

## Local coverage command

After a successful build:

```bash
dotnet build ResearchTrack.sln -c Release --no-restore
./scripts/coverage.sh --no-build
```

`coverage.sh` runs tests with:

```text
Category!=DatabaseIntegration
```

and generates a merged report under:

```text
artifacts/coverage/
├── coverage-summary.md
├── coverage-metrics.env
├── README.md
└── report/
    ├── index.html
    ├── Cobertura.xml
    ├── SummaryGithub.md
    ├── Summary.json
    ├── Summary.txt
    └── Summary.csv
```

## Coverage scope

The merged report focuses on production ResearchTrack assemblies. The following are excluded from the evidence calculation because they are not meaningful unit-test targets:

- test assemblies and `ResearchTrack.Testing`
- generated EF migrations
- `Program.cs` composition roots
- generated `obj`/`*.g.cs` files
- development-only seeder/database-check tools

This prevents the team from inflating or depressing the number with code that is outside the intended unit-testing scope.

## Threshold policy

No arbitrary coverage percentage is committed before a measured baseline is reviewed. The script supports opt-in gates:

```bash
COVERAGE_MIN_LINE=80 COVERAGE_MIN_BRANCH=70 ./scripts/coverage.sh
```

If either configured threshold is missed, the script exits non-zero. Until the team finalizes a defensible baseline, CI reports coverage without imposing a made-up gate.

## GitHub Actions

`backend-ci.yml` now:

1. restores and builds the backend;
2. runs the fast unit + mocked suite with coverage;
3. merges all Cobertura files with the repository-pinned ReportGenerator tool;
4. appends the concise coverage result and ReportGenerator summary to the GitHub Actions step summary;
5. uploads the complete unit-test coverage evidence as a build artifact even if a later CI step fails.

Database integration tests remain a separate workflow concern because they require configured MySQL test databases and the `RESEARCHTRACK_TEST_*_CONNECTION` variables.

## Mutation-testing readiness

Pamudi's Stryker work should consume the same fast deterministic unit/mock layer. Mutation testing should target business logic rather than generated migrations, startup wiring or DTO-only code. The coverage report is useful for selecting the initial mutation namespaces, but line coverage and mutation score are different metrics and should not be conflated.
