# SE3112 — Genuine new-code coverage evidence for SonarQube Cloud

## Verified incident (provided GitHub Actions artifact)

`backend-sonarqube-analysis (1).zip` contains exact SonarCloud API records:

- Project: `Nimsara-Jayarathna_researchtrack-backend`, branch `main`.
- Analysis ID: `ae49e808-6b21-4b84-847d-00bac82b336d`.
- Compute engine: `SUCCESS`.
- Quality gate: `ERROR` because `new_coverage` **0.0%** vs required **80%**.
- Five other recorded gate conditions were `OK`: new reliability,
  security, maintainability, duplication and reviewed hotspots.
- `ignoredConditions` was `false`, even though the project's small-change
  ignore setting was enabled in the screenshot.
- Pre-integration measures: **83 code smells**, **95 minutes debt**,
  **2.4% duplicated lines**, **26,822 LOC**. These are observed values from
  this cloud run, not estimates.

## Coverage implementation (new scanner run required)

The SonarQube workflow **reuses** the seven existing, coverlet-enabled xUnit
v3 unit projects. It does not alter the test projects, shared test toolchain,
unit/mocking CI pipeline, mutation runs or k6 workflows.

The scanner's steps are:

1. Restore eight production projects and seven unit projects.
2. Begin SonarScanner for .NET 11.3.0 with
   `sonar.cs.opencover.reportsPaths=artifacts/code-quality/sonar/ci/coverage/**/coverage.opencover.xml`.
3. Build the same eight production projects in Release mode.
4. Run all seven existing unit suites using `dotnet test`, the existing
   `Category!=DatabaseIntegration` filter, and the Coverlet collector's
   `XPlat Code Coverage;Format=opencover` option.
5. Check that every suite succeeded and emitted a nonempty OpenCover report.
6. End the scanner and import coverage; subsequently query the exact
   analysis's quality gate and overall/new coverage metrics.

The workflow uploads XML reports and `coverage-collection-summary.md` inside
`backend-sonarqube-analysis`. An uploaded report only proves collection;
the Sonar dashboard is the source of actual coverage percentages.

## Acceptance criteria for the next real run

- Seven unit suites complete successfully and seven `coverage.opencover.xml`
  reports are attached to the artifact.
- Scanner end logs report successful OpenCover import.
- `sonarqube-summary.md` contains measured `new_coverage` and the
  `new_coverage` condition from `quality-gate.json`.
- The default **≥80% coverage on new code** gate passes only if at least 80%
  of coverable new code is actually exercised. If lower, write additional
  legitimate tests with the unit-testing contributor; do not adjust the
  threshold or claim that merely importing coverage fixed the gate.
- Preserve branch and scan scope when comparing before/after results.

## Commands

```bash
bash ./scripts/code-quality/sonar-cloud.sh self-test
# GitHub Actions: Backend SonarQube Cloud Analysis → Run workflow → main
```

The self-test uses mocked executables; the .NET 10 build and live scanner
must pass in GitHub Actions before marking this integration validated.
