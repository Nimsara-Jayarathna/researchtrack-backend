# SE3112 — SonarQube Cloud .NET 10 Integration

This integration performs real **build-aware C# analysis**, uploads results to
**SonarQube Cloud Free**, waits for the cloud Compute Engine analysis, and
exports measured metrics and quality-gate evidence to GitHub Actions.

## 1. Exact scope / what is preserved

- **New** `.github/workflows/backend-sonarqube.yml` is independent of the
  existing deployment, unit, mutation and performance workflows.
- **New** `.github/workflows/backend-code-quality.yml` makes the existing
  CSharpier/Roslyn `check ci` command runnable in GitHub Actions. The uploaded
  feature ZIP had the scripts but did **not** contain this workflow file.
- The scanner compiles eight explicit production `.csproj` files: one shared
  BuildingBlocks, Gateway, and the six services. **In the same scanner session**
  it runs the seven *existing* unit-test projects with Coverlet to produce real
  OpenCover evidence. Test project files and other students' testing workflows
  are untouched. Mutation, performance, integration-database suites, deployment,
  and infrastructure tooling are not executed.
- Only the main branch is analyzed by this workflow; this is compatible with
  a SonarQube Cloud Free main-branch baseline and avoids rewriting it from a
  feature branch. `workflow_dispatch` must be run with branch **main**.
- The `sonar.scanner.scanAll=false` setting prevents incidental Python and
  other non-.NET files being discovered by the .NET scanner. EF migrations and
  compiler-generated files are excluded from Sonar metrics, not removed from
  compilation.
- SonarQube imports the real `coverage.opencover.xml` reports produced by
  `coverlet.collector`; the scanner's C# OpenCover property points to those
  reports. The existing tests use `Category!=DatabaseIntegration`. Reporting
  coverage is **not** a guarantee of an 80% pass: previously untested changed
  code must be covered by genuine tests to satisfy the default gate.

## 2. Required one-time SonarQube Cloud settings

In SonarQube Cloud open the ResearchTrack project:

1. Sign in to the **correct organization** and check the **project key** and
   **organization key** in the project analysis/settings pages.
2. Go to **Administration → Analysis Method**. Disable **Automatic Analysis**;
   don't use both automatic and CI-based analyses for the same Sonar project.
3. Generate a personal analysis token in My Account → Security. It must have
   permission to execute analysis on the ResearchTrack project.
4. Check **Administration → New Code**: the quality gate may say *Not computed*
   until a proper new-code baseline exists. Do not fabricate a gate result.

In the **researchtrack-backend GitHub repository**, open
**Settings → Environments → production**. Create the `production` environment
if it does not exist. Under this environment, configure **Environment secrets**
and **Environment variables** (not repository-level Actions values).

The SonarQube workflow explicitly assigns `environment: production` to its
`sonar-analysis` job, which is required for GitHub to expose these credentials.
The separate native CSharpier/Roslyn workflow does **not** use the production
environment and remains unchanged.

| Type in `production` environment | Name | Meaning |
|---|---|---|
| Secret | `SONAR_TOKEN` | The SonarQube Cloud token. Never print/commit this value. |
| Variable | `SONAR_PROJECT_KEY` | Exact project key from SonarQube Cloud. |
| Variable | `SONAR_ORGANIZATION` | Exact organization key from SonarQube Cloud. |
| Variable (optional) | `SONAR_REGION` | Leave unset for sonarcloud.io (EU); set `us` only for US. |
| Variable (optional) | `SONAR_ENFORCE_GATE` | **false for first baseline**; set `true` later to fail CI on red SonarQube gates. |


**Environment protection note:** if `production` requires manual reviewers,
the SonarQube analysis job will wait for that approval before it starts. If
production has branch restrictions, allow `main` for this analysis workflow.
This quality analysis does not deploy the application, but GitHub records the
job as using the `production` environment.

**No VPS, SSH tunnel, Docker, PostgreSQL or SonarCloud server credentials**
are required. The scanning tool is the official SonarScanner for .NET, pinned
at **11.3.0**, installed into the *ignored* `artifacts/code-quality/sonar-tools/`
directory. This does not change the repository's EF/Stryker/CSharpier tool
manifests or the normal .NET build configuration.

## 3. First real run (GitHub Actions)

1. Make sure your code-quality feature branch has passed local
   `bash ./scripts/code-quality.sh check` following your formatter pass.
2. Commit these changes, raise a PR, and merge into `main` using the normal
   team review process. The new Sonar workflow is **main only**.
3. Open GitHub → **Actions → Backend SonarQube Cloud Analysis**.
4. Choose **Run workflow → main** or wait for the next main push.
5. Expect Checkout → .NET 10 → Java 17 → Validate credentials → Install scanner
   → Restore eight production and seven existing test projects → Scanner begin
   → Build eight production projects → Run seven existing unit test suites with
   Coverlet OpenCover collection → Scanner end (import reports) → Fetch results
   → Upload analysis and OpenCover evidence.
6. Review the GitHub run summary and download the
   `backend-sonarqube-analysis` artifact. Open SonarQube Cloud Summary/Issues.

The first run is intentionally **nonblocking for Sonar's quality gate** when
`SONAR_ENFORCE_GATE` is unset/false. It still fails on missing secrets,
installation issues, compiler errors, upload errors and incomplete API reporting.
The summary shows the **actual** gate status as reported by SonarQube, even
when gate enforcement is disabled. The scan now also fails if the existing unit
suite fails or any of the seven coverage reports is absent; it never pretends
that collector failures are measured 0% coverage.

When a baseline is established and the SonarQube gate has usable conditions,
set **production environment variable** `SONAR_ENFORCE_GATE=true`. From then on, SonarScanner
waits up to 600 seconds and fails the GitHub workflow if the SonarQube quality
gate fails. Since genuine coverage is now imported, inspect the `new_coverage`
condition after the next run; a red 80% condition reflects actual uncovered
new code if the report was successfully imported.

### Reports and what they prove

On CI the ignored folder `artifacts/code-quality/sonar/ci/` contains:

- `report-task.txt`: scanner upload metadata (Compute Engine task ID).
- `ce-task.json`: the compute-engine task result for this exact uploaded scan.
- `quality-gate.json`: SonarQube gate status queried by **analysis ID**, not
  a potentially stale project status.
- `measures.json`: actual project measurement API response, including
  overall/new coverage where SonarQube reports those metrics.
- `sonarqube-summary.md`: quality gate's individual condition values plus
  lines of code, complexity, duplications, code smells and technical debt.
- `coverage-files.txt`: seven repo-relative paths to collected OpenCover files.
- `coverage-collection-summary.md`: evidence of collection; not a substitute
  for actual calculated SonarQube coverage.
- `coverage/<unit-project>/<guid>/coverage.opencover.xml`: seven raw coverage
  reports, one from each existing unit-test suite.

Some metrics can be absent. The report says **not reported**, not zero.
Security and reliability findings are best reviewed directly in the project
**Issues** view (click the rule, file path and line number before code edits).

## 4. Manual scanner run (optional; Windows Git Bash)

The GitHub workflow is recommended: it has full Git history and correct branch
metadata. If a local baseline is needed, first check out the `main` branch and
set env vars in Git Bash, without printing the token:

```bash
export SONAR_PROJECT_KEY='COPY_EXACT_PROJECT_KEY_FROM_CLOUD'
export SONAR_ORGANIZATION='COPY_EXACT_ORG_KEY_FROM_CLOUD'
read -rsp 'SonarQube token: ' SONAR_TOKEN; echo
export SONAR_TOKEN
export SONAR_ENFORCE_GATE=false

bash ./scripts/code-quality/sonar-cloud.sh doctor
bash ./scripts/code-quality/sonar-cloud.sh setup
bash ./scripts/code-quality.sh sonar local-first

# API reporting needs jq + curl (the GitHub hosted Ubuntu runner has both):
bash ./scripts/code-quality/sonar-metrics.sh local-first
unset SONAR_TOKEN
```

Use `SONAR_REGION=us` only if your SonarQube project is in the US instance.
Do not run concurrent local scans and GitHub scans into the same project.

## 5. Capture a before/after assessment

- Save a screenshot of the **first valid CI-based C# analysis**. The prior
  automatic analysis showed approximately 36k LOC, 45 security issues (E),
  28 reliability issues (C), 5 maintainability issues (A), and 1.7% duplication.
  **Do not use that automatic-analysis snapshot as a like-for-like .NET baseline.**
  Python auto-analysis warnings and scan scope can change the figures.
- Select 1–3 **confirmed** security/reliability findings. Record issue rule,
  risk, affected file/line and the reasoning behind a safe fix.
- Fix only production application source in a separate commit. Do not change
  students' tests or testing workflows.
- Merge/re-run the same CI-based scanner and compare the same metrics,
  project key, analysis mode and scope between the two CI snapshots.
- Capture GitHub Actions check results and the SonarQube dashboard before/after.

## 6. Troubleshooting

| Symptom | What to check |
|---|---|
| Scanner tool installed, but `Please specify the command 'begin' or 'end'` during setup | The old script incorrectly called `dotnet-sonarscanner --version`, which exits 1. The corrected script checks `dotnet tool list --tool-path ...`; no token or region change needed. |
| `SONAR_TOKEN is missing` | Check **Settings → Environments → production → Environment secrets**, then confirm the job has `environment: production`. |
| Project/organization not found | Correct values, token permissions, and `SONAR_REGION`. |
| `Automatic Analysis` conflict | Disable automatic analysis in SonarQube Cloud before CI scanning. |
| Compilation fails in scanner build | Open `Restore and analyze all 8 production projects` logs. All builds use `TreatWarningsAsErrors=false` **only in the scanner context**, without changing normal CI. |
| Quality gate fails at 80% new coverage | Open `quality-gate.json` and check `new_coverage`. The old analysis `ae49e808-6b21-4b84-847d-00bac82b336d` had 0% and **no imported reports**. The updated workflow imports seven real reports, but may still miss lines that existing tests do not exercise. Add focused tests via the unit-testing contributor; do not fabricate coverage, disable rules, or alter the gate threshold. |
| Scanner fails before upload | Check the first failing `dotnet test` suite or `ERROR: Expected one non-empty OpenCover report`. This is intentional fail-closed behavior. |
| Coverage XML is present but Sonar coverage not reported | Inspect scanner end logs for OpenCover import messages and path matching. The glob is `artifacts/code-quality/sonar/ci/coverage/**/coverage.opencover.xml`. |
| Quality gate red despite small-change exception | `quality-gate.json` contains `ignoredConditions`; this flag was `false` on the preceding analysis. The exception depends on new-line counts, not solely new lines requiring coverage. |
| Gate says `Not computed` | Check new-code definition and that a second processed analysis is present. |
| Can't read metrics | The scan may still be processing; inspect CE task and token Browse permission. |
| Dashboard seems to show Python issues | Confirm CI scanner's `scanAll=false` and latest analysis type/time. |

### Local script tests (no NuGet or live cloud needed)

```bash
bash ./scripts/code-quality/sonar-cloud.sh self-test
```

This uses fake `dotnet`, `curl` and SonarScanner executables and proves only
control flow, seven mocked coverage suites, missing-report/test failure
handling, OpenCover path wiring, red-gate selection, and report parsing. It cannot
prove real scanner compatibility or successful cloud uploads. Genuine evidence
must come from your GitHub Actions run.

**Official guide:** https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/scanners/sonarscanner-for-dotnet/using
