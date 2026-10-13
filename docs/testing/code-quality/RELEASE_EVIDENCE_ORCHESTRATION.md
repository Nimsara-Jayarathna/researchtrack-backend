# ResearchTrack — Full CI optimization and secure release orchestration (October 2026)

This guide covers all four planned CI optimization phases **and** the Azure
infrastructure paused-runtime monitoring fix. Changes are deliberately limited
to `.github/workflows/`, `scripts/code-quality/`, `deploy/automation/`,
`deploy/azure/scripts/release-evidence.py`, the regression checks and these docs.
No microservice implementation, Unit/Mutation/k6 suites, EF migrations or
existing backend release secrets are changed.

## Phase 1 — PR-native quality without duplicate merge runs

* `Backend Code Quality` runs on pull requests, merge queues and manual dispatch;
  no unconditional `main`/`develop` push trigger. `check-style` is read-only,
  verifies CSharpier and Roslyn, and does not build eight production projects.
  Local `fix`/`check` and analyzer remain available.
* PR CI and quality concurrency cancels superseded PR runs. The release and
  infrastructure concurrency groups never cancel a production mutation.
* `Backend CI` performs solution build, tests and coverage. Kafka integration
  remains required. `Backend CI` is a callable reusable workflow.

## Phase 2 — Conservative shared change classification

* `deploy/automation/change-scope.py` understands application / Sonar / PR
  DevOps scope. Invalid/missing Git history, unexpected paths, force pushes,
  and unsupported events all result in `execute=true` (run work).
* PR required workflow **always exists**; no top-level `paths:` filters that
  could leave branch-protection checks pending.
* Only individual offline DevOps verification steps may be skipped on source-
  only pull requests. Full release CI runs the complete DevOps suite.

## Phase 3 — Change-scoped deployment and VM preservation

* Test/Production have an inexpensive pre-deployment eligibility stage and
  allow manual forced execution.
* Azure image builder still uses its fingerprint-aware service selection.
* Production release still verifies `main` exact SHA, runtime power snapshot,
  deferred Azure credentials, DB migrations, post-deployment checks, VM and
  Container Apps power-state restoration; deployment is serialized with
  infrastructure reconciliation.
* The Azure infrastructure workflow uses `infrastructure-runtime-guard.py`
  to snapshot the VM, classify all seven running/stopped Container Apps,
  avoid false application-down alerts when intentionally paused and restore
  the original VM state when safe. **Partially running and unknown states fail**;
  all apps running with broken `/metrics` endpoints still fail. No blanket
  disabled health checks or restarted application apps.

## Phase 4 — Attested CI evidence with mandatory fallback

The reusable `Backend Production Release Validation` workflow now consists of:

1. **Unprivileged evidence probe:** read-only GitHub API calls check `main`
   equals the current immutable release SHA.
2. **Reuse only if proven:** a previously validated release run with successful
   unit/coverage + isolated Kafka + exact-source eligibility jobs, OR both
   verified PR workflows linked to the associated merged PR where the GitHub
   **check-suite commit trees** (including synthetic test-merge revisions) and
   corresponding PR-head trees match the release's immutable Git tree.
   The code-quality job and full Backend CI+Kafka checks must be successful.
   Forks, unknown check/job names, incomplete API pagination, stale main,
   unmerged PRs, incomplete checks and missing permissions all reject reuse.
3. **Fail-closed fallback:** on any uncertainty the standard **full Backend
   CI**, including coverage, Kafka and release DevOps tests, executes again.
   `workflow_dispatch` always runs full CI for reproducible manual releases.
4. **Exact SHA source gate:** irrespective of how CI succeeded, the existing
   `release-policy.py` validates trusted repository, approved branch, event,
   current GitHub main SHA and successful preceding required checks.

This is **not** trusting a PR number alone or downloading untrusted PR artifacts.
Two different Git SHAs can have the same Git tree, but the automation requires
all tested source files, build configuration and workflow inputs to match. The
PR optimization deliberately rejects changes where the source tree cannot be
established as identical. A squash merge may therefore need full release CI.

### SonarQube release gate

`Backend SonarQube Cloud Analysis` remains the **only main-branch scanner**. On
`main`, it now always sets `SONAR_ENFORCE_GATE=true` even if the Production
GitHub variable is false. The CLI waits for a real SonarQube quality-gate
response, so a successful scan job means the gate passed.

For an eligible application deployment, `backend-deploy-azure-production.yml`
first performs the required release validation, then runs a read-only
`sonar_gate` job **before building images, touching Azure, or loading production
secrets**. It checks whether scanning is required for the actual push diff:

* If definitely unrelated to analysis, it records `not required`.
* Otherwise it polls for a successful, completed
  `Backend SonarQube Cloud Analysis` **scan job on exactly this `main` SHA**
  using the GitHub Actions API. A red gate, skipped scan, unrelated workflow,
  stale SHA, missing API permission or timeout blocks the release.
* It never triggers a second SonarScanner analysis and never uses an
  untrusted `workflow_run` event or GitHub Actions artifact to authorize Azure.

The `production` GitHub Environment still supplies `SONAR_TOKEN`, project and
organization. The release's read-only API probe uses the built-in
`GITHUB_TOKEN` with `contents:read`, `actions:read`, `pull-requests:read`.
No new personal access tokens or Azure credentials are required.

**Limitation:** separate simultaneous production/infrastructure workflows may
both start full fallback CI when there is no reusable evidence at scheduling
time. An existing successful same-SHA release validation can be reused by the
second run; complete deduplication of concurrent cache misses would require a
single orchestrator and is not claimed here. Also, CI reuse from PR tests
requires exact tree equivalence; passing PR statuses alone are insufficient.

## Operational checks

```bash
python3 deploy/automation/test_change_scope.py
python3 deploy/azure/validation/test_release_evidence.py
python3 deploy/azure/validation/test_release_policy.py
python3 deploy/azure/validation/test_workflow_release_gates.py
python3 deploy/azure/validation/test_infrastructure_runtime_guard.py
python3 deploy/azure/validation/test_monitoring_reconciliation.py
bash ./scripts/code-quality.sh self-test
```

In GitHub Actions, verify **both** a normal C# PR and a docs-only PR, a merge
with identical-tree reusable evidence, a squash merge with fallback CI,
SonarQube gate `OK`/red, skipped app deployment, and intentionally stopped
production VM/Container Apps. Review the exact expected job dependencies and
Azure power-state restoration before enabling stricter branch protection.
Production branch protection must still require `Backend CI` and
`Backend Code Quality` PR checks. If a `merge_group` is used, require those
checks for merge queue events too.

**Verification scope:** repository-local regression checks are offline, using
mock GitHub/Azure APIs. A real GitHub run and Azure state restoration still
need to be confirmed on the installed repository. This code cannot guarantee
unmeasured future Actions minutes or live availability.
