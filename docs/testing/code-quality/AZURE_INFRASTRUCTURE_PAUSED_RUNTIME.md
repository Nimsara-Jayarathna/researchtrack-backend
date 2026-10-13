# Azure Production infrastructure: paused-runtime validation and power restoration

## Why the previous infrastructure workflow failed

The VM stack itself completed its MySQL (TLS), Kafka (TLS), Prometheus, Grafana and Nginx checks. The previous workflow set `EXPECT_APPS=true` when all seven **Container App resources existed**. However, existence does not imply `Running`. Prometheus returned HTTP 404 for four application scrape endpoints, which triggered `ResearchTrackServiceUnavailable` and failed stack validation. A 404 may also indicate a genuinely incorrect running service / ingress / `/metrics` route, so it must not be universally suppressed.

Previously `deploy/azure/scripts/vm-run.sh` started a deallocated VM and **left it running** after infrastructure reconciliation, regardless of the original VM state. `vm-run.sh` retains its established behavior for existing callers; the infrastructure workflow adds a narrowly scoped final restoration stage instead.

## New infrastructure workflow behavior

1. The trusted production workflow performs **exact-main-commit required CI**, Bicep compilation and source verification before Azure access (unchanged).
2. Before `What-If`, deployment, or any Azure VM Run Command, `infrastructure-runtime-guard.py snapshot` reads the **existing VM power state** and **current Container App power states**; the snapshot is local to the runner and never uploaded as an artifact.
3. Bicep/VM reconciliation proceeds with the existing implementation.
4. The guard classifies application state through Azure Resource Manager, not HTTP responses:

   | State | Outcome |
   |---|---|
   | All seven Running | Strict seven-service `/metrics`, `/health/ready`, network and critical-alert checks |
   | All seven Stopped | Infrastructure-only checks; skip app-specific readiness/scrapes and their two intentional availability alerts |
   | No apps provisioned | Infrastructure-only checks (bootstrap) |
   | Some apps provisioned and every existing app Stopped | Infrastructure-only checks with explicit partial-deployment message |
   | Any mixture of Running/Stopped, partially Running, unknown or transitioning | **Fail closed**, report the actual states; never pretend production is healthy |
   | All Running but `/metrics` returns 404 | **Fail strict validation**; investigate the deployed revisions, ingress and routes |

5. Kafka, MySQL, Grafana, Prometheus configuration, exporter health, private connectivity, Nginx, and **non-application critical alerts** are still validated even if all application replicas are intentionally stopped.
6. On success **or failure**, a final `always()` step restores the infrastructure VM's original state when safe. If it was deallocated it is deallocated again; if it was stopped (allocated) it is stopped without deallocating; if it was running but is now off it is started. A newly created VM is intentionally left up for first-time provisioning.
7. Before shutting down a previously paused VM, the guard requires that **all observed app states are identical to those captured** and that no app is running. Changes, unknown states, unmanaged ResearchTrack production apps, mismatched VM identity, or Azure errors **block VM shutdown**. This avoids accidentally taking down newly started services.

This policy **does not make a mixed or unhealthy production state pass**. If four apps report `Stopped` and three `Running`, the workflow will stop with an explicit mixed-state diagnostic. Use the existing production runtime-power workflow to restore a consistent desired state, or investigate broken service revisions. Do not disable mandatory monitoring checks.

## No additional secrets needed

Existing GitHub Environment `production` Azure OIDC settings are used. The workflow assumes the resource naming already established by the production Bicep parameters: `rg-researchtrack-prod` / `vm-researchtrack-infra-prod`. It installs the Azure Container Apps CLI extension before attempting to list app states.

## Offline regression checks

```bash
python3 deploy/automation/test_change_scope.py
python3 deploy/azure/validation/test_infrastructure_runtime_guard.py
python3 deploy/azure/validation/test_monitoring_reconciliation.py
python3 deploy/azure/validation/test_release_policy.py
python3 deploy/azure/validation/test_workflow_release_gates.py
bash ./scripts/code-quality.sh self-test
bash ./scripts/code-quality/sonar-cloud.sh self-test
```

Regression scenarios cover all seven Running/Stopped, partial/unknown/mixed states, unexpected app activation, preexisting VM running/stopped/deallocated, restored VM identity, and first infrastructure creation. The new guard tests run in `Backend CI`'s conditional DevOps check stage and in exact-commit release CI.

## Production verification after merging

- Verify PR `Backend CI` and `Backend Code Quality` checks are green.
- On a **deliberately paused** environment, confirm the classification prints `paused`, infrastructure-only checks execute and the final VM state equals the original state. Do not rerun until Azure subscriptions/permissions and current VM/apps state are understood.
- On an **intentionally online** environment, all seven app scrape endpoints must pass. If 404 remains, check ingress and `/metrics` from the private VM VNet; this package does not mask genuine endpoint failures.
- Check GitHub Actions `Azure Infrastructure - Production` on the merge; it is triggered only when its existing or new infrastructure input paths change. The workflow's deploy serialization and exact-SHA release policy remain unchanged.
- If the runner is terminated outside normal GitHub failure handling (e.g., machine lost or workflow forcibly canceled), `always()` cleanup is not guaranteed; verify Azure VM and Container App power states manually.

## CI/CD savings applied on the same branch

The 10-file optimization set is documented in `CI_WORKFLOW_OPTIMIZATION.md`. Native CSharpier/Roslyn quality runs on PR / merge queue (not again on each push), PR checks cancel outdated revisions, DevOps checks are conditional on source scope, docs/CI-only merges do not start production/Test deployment or Sonar scans, and required exact-commit production release validation is intentionally retained. Mutation and heavy k6 workflows are untouched.

**Scope:** Only workflows, their supporting quality/infrastructure scripts, docs and relevant *workflow regression tests*. No application service code, EF migrations, authentication logic, unit-test suites, mutation testing, Kafka test logic or load tests are modified.
