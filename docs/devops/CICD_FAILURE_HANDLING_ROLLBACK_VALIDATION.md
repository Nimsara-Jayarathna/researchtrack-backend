# TASK 11 — Production CI/CD failure handling and rollback validation

Date: 2026-10-12. Branch: `integration/main-kafka-production-20261011`.
Base HEAD: `1810f09535d6a6d00947bcf84d18fde473e40c39`; earlier local TASK 02–10 work preserved.

## Result and scope

Offline CLI/workflow regressions confirm that failed CI, source eligibility, migrations,
ARM reconciliation, revision verification, smoke or restoration cannot become a
successful release merely because cleanup/reporting succeeds. Skipped optional smoke
continues to mean **final acceptance pending**.

Two local corrections were made:

1. Revision rollback previously printed `failed, rolled back` even after failed recovery
   or when no previous revision existed. The new regression first **FAILED** against
   that code (`deploy-before-fix.log`). Recovery now distinguishes completed revision
   recovery, retained previous revision, incomplete recovery and absent rollback target.
   Activation/deactivation also reads back the active flag after CLI success: a command
   returning zero without changing state cannot prove recovery. The rollout still fails.
2. Release API/reporting errors previously left no release-result summary. They already
   failed the reporting job. A sanitized fallback now retains caller deployment/smoke
   conclusions, explicitly marks missing evidence and final acceptance unverified, and
   still exits 1. Exception text, API bodies and tokens are not copied into that report.

No workflow trigger, permission, credential, environment, concurrency or automatic
rollback policy was changed. There is no new rollback on smoke failure, cancellation
or missing metadata. No schema rollback, Kafka offset reset or durable-event deletion.

## Failure-handling matrix

**PASS here means the stated offline assertion passed.** Static graph checks do not
execute GitHub's scheduler or third-party Actions. Cooperative signal tests do not
simulate runner loss/SIGKILL. No production failures were generated.

| # | Scenario | Actual evidence / result | Behavior and limitation |
|---|---|---|---|
| 1 | Required CI failure | **PASS**, release-policy status subcases, workflow dependency checks and summary regressions | Build depends on validation; deploy depends on build + validation. Failed/cancelled/skipped/inconclusive required CI rejects eligibility. |
| 2 | Kafka test failure | **PASS**, required Kafka/source graph and failed/skipped/cancelled Kafka summary cases | Kafka validation is unconditional in required CI; fixture runner enforces expected executed/passed counts. Actual Kafka suite deliberately not rerun for this synthetic task. |
| 3 | Incorrect release SHA | **PASS**, malformed/stale/source mismatch policy cases and new wrong-run-SHA reporting case | Exact SHA checkout/current source recheck precedes privileged access; mismatched reporting metadata fails with incomplete summary. |
| 4 | Docker build failure | **PASS, static dependency/no-bypass checks**; Docker Action execution **NOT EXECUTED** | A failed reusable build cannot satisfy deploy prerequisites; no `always()`/`continue-on-error` bypass. Missing published source images also fail the real deployer with synthetic registry CLI. Not an executed build-push Action failure. |
| 5 | Migration failure | **PASS**, real deployer with synthetic migration ARM PUT failure | Nonzero exit, auth migration marked FAIL with duration, no rollout attempted, remaining services NOT EXECUTED, rendered secret specs removed. Partial/committed database migration behavior is not simulated. |
| 6 | Azure deployment command failure | **PASS**, ARM failure despite healthy matching existing revision | Failure flag remains fatal; previous revision remains active; a healthy app cannot conceal ARM failure. |
| 7 | Unhealthy application revision | **PASS**, provisioning Failed, health Unhealthy and revision-query error cases | Nonzero exit; named app/revision diagnostics; known failed revision deactivated when possible; captured previous revision retained. |
| 8 | Readiness timeout | **PASS**, 60-poll unhealthy revision test, HTTP readiness synthetic responses, VM readiness tests | Revision timeout names the app/revision; public check has 10 attempts and rejects non-200/TLS/network failures. Sleep is stubbed; real elapsed deadlines not tested. |
| 9 | Kafka dependency unavailable | **PASS**, new unhealthy broker and private listener timeout cases | Infrastructure acquisition fails when Kafka health/9092 connectivity fails. App Degraded/200 policy is unchanged; that response alone does not prove Kafka health. |
| 10 | k6 smoke failure | **PASS**, mocked VPS k6 exit failure + evidence retrieval; Gateway wrapper restoration; summary failure cases | Nonzero persists through report retrieval and cleanup; report download attempted; Gateway/runtime restore independently. No automatic multi-service rollback. |
| 11 | k6 cancellation | **PASS**, synthetic INT and TERM sent to actual Gateway/runtime session wrappers; cancelled summary case | Wrappers restore configuration/power state and exit 130. Tests exercise wrappers separately, not the whole GitHub/SSH process tree. Hard termination remains NOT VERIFIED. |
| 12 | k6 skipped | **PASS**, caller switch/dependency assertions and skipped summary case | Optional auto-smoke skip remains `DEPLOYMENT PASSED; SMOKE SKIPPED — FINAL ACCEPTANCE PENDING`, not full PASS. |
| 13 | Runtime restoration failure | **PASS**, actual runtime-session with failed synthetic app-stop; actual workflow safeguard bodies | Successful work cannot hide restore failure; nonzero/CRITICAL diagnostics, snapshot and unfinished ownership retained; bounded retries asserted. |
| 14 | Gateway restoration failure | **PASS**, successful synthetic k6 followed by three failed wrapper restores; actual workflow safeguard body | Failure remains fatal; snapshot/ownership remain for safeguard/operator recovery. Gateway restore and power restore are independent steps. |
| 15 | Concurrent infrastructure deployment | **PASS, static lock validation**; live scheduler overlap **NOT EXECUTED** | App/infrastructure/runtime/performance entrypoints share `researchtrack-azure-production`, non-cancelling queue. CLI operators and old workflow copies remain outside that guarantee. |
| 16 | Failed deployment with previous healthy revision | **PASS**, whole-deployer ARM/health failures and extracted actual revision helper cases | Existing active revision is retained idempotently; inactive previous revision can be reactivated in the mock. Failed mutations, zero-exit/no-state-change, missing target and unknown failed identity are reported accurately. Active flag is not complete health/traffic proof. |
| 17 | Release summary metadata unavailable | **PASS**, new mocked API exception, wrong SHA and unavailable output tests | Explicit fallback when writable; reporting exits 1; if even summary output is unavailable, sanitized stderr still identifies reporting failure. No invented durations or CI acceptance. |
| 18 | Manual deployment cancellation | **PASS, synthetic restoration/graph assertions**; in-flight Azure cancellation **NOT VERIFIED** | Ownership-aware `always()` bodies and cooperative session traps restore safely in mocks. Application deploy is not wrapped in runtime-session; GitHub cancellation/runner failure can interrupt Azure commands or prevent final steps. No automatic cancellation-driven schema/revision rollback added. |

## Existing control flow and retained evidence

`backend-deploy-production.yml` → required release validation → image build →
`backend-deploy-azure-production.yml` deployment/verification/restoration → optional
`azure-performance-k6-reusable.yml` smoke/restoration → `release_summary` with `always()`.
Summary depends on both deploy and smoke results and cannot change their conclusions.
There is no `continue-on-error` on these required deployment paths.

All planned database-owning services migrate **before the first app rollout**. A failed
migration prevents rollouts; previously successful migrations are not undone. Services
then roll out sequentially. A later failure does not undo earlier successful service
updates. Schema/app-scope configuration and cross-service compatibility therefore need
operator review, even when a previous revision is active.

Artifact controls already configured:

| Evidence | Existing retention / behavior |
|---|---|
| Unit/mocking coverage/TRX | `unit-mocking-coverage-<run>-<attempt>`, 14 days, upload `always()` when files exist |
| Isolated Kafka evidence | `isolated-kafka-integration-<run>`, 14 days, upload `always()`; local synthetic task does not create a real Kafka artifact |
| Deployment power snapshot | `azure-production-runtime-before-deploy-<run>-<attempt>`, 7 days, uploaded before first power mutation; upload failure blocks acquisition |
| Smoke power snapshot | `azure-k6-runtime-state-<run>-<attempt>`, 7 days, before acquisition |
| Optional Gateway snapshot | `azure-k6-gateway-state-<run>-<attempt>`, 7 days, before exemption changes; automatic smoke has bypass disabled |
| k6 reports | `k6-<profile>-<run>-<attempt>`, 14 days, upload `always()`; VPS retrieval attempted after failed k6; missing reports warn and are not fabricated |
| Migration/rollout/image provenance | Existing GitHub step summaries and run logs; secret-bearing rendered parameter/spec files are deleted by the deployment EXIT trap |

Artifact preservation is an attempted control, not guaranteed when upload/network fails,
the runner disappears or cancellation prevents subsequent steps. Retain incident evidence
before the configured expiry. No raw runtime environment/spec files should be published.
Application console logs are diagnostic output: existing app logging/masking must remain
appropriate; synthetic tests do not establish that every live log is free of secrets.

The repo uses Single revision mode. Azure documents that normal failed single-mode
updates keep traffic on the old ready revision; this does not undo app-scope secrets,
registry/ingress settings or database changes. See [Container Apps revisions](https://learn.microsoft.com/en-us/azure/container-apps/revisions).
The existing best-effort rollback sequence can deactivate the suspect revision before
reactivating an inactive previous revision; if reactivation fails, service availability
is not guaranteed. Its action ordering was not redesigned in this task. The new table
reports that recovery as incomplete rather than claiming success.

Cooperative INT/TERM trap tests passed, but GitHub escalates termination and eventually
kills processes. A trap/`always()` is not a guarantee against force cancellation or runner
loss. Use captured artifacts for reviewed recovery. See [GitHub cancellation behavior](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-cancellation).

## Executed validation

All commands ran locally with fake Azure/Docker/SSH/curl/provider responses. No authenticated
Azure CLI, live SSH, GHCR writes or GitHub Actions dispatch was performed.

| Suite | Actual result |
|---|---|
| `test_release_policy.py` | **PASS**, 25 unittest methods, including new reporting-error regressions |
| `test_workflow_release_gates.py` | **PASS**, 31 unittest methods, real extracted workflow shell bodies with stubs |
| `test-deploy-image-selection.sh` | **PASS**, 24 assertion groups; pre-fix regression **FAIL** retained separately |
| `test-runtime-power.sh` | **PASS**, 13 groups, including both INT/TERM subcases and failed restoration |
| `test-runtime-readiness.sh` | **PASS**, 6 groups, including Kafka unhealthy/private listener unavailable |
| `tests/performance/test-run-vps.sh` | **PASS**, mocked k6 success/failure/report download/credential cleanup |
| `tests/performance/test-gateway-exemption.sh` | **PASS**, mock capture/enable/restore/verify/failure/cancellation cases |
| Bash syntax, seven relevant scripts/tests | **PASS** |
| Raw actionlint 1.7.12 | **FAIL**, existing five unsupported `queue` findings and five SC2129 style findings |
| Qualified actionlint | **PASS**, only `unexpected key "queue"` and SC2129 excluded; no other findings |
| Raw ShellCheck 0.11.0, final code with external sources resolved | **FAIL**, informational indirect-trap SC2329 and caller-provided environment SC2153 findings |
| ShellCheck at warning/error severity, sources resolved | **PASS**; intentional child-shell expansion documented and new fixture diagnostics corrected |
| `git diff --check` | **PASS** |
| Live GitHub scheduler/Docker Action/Azure failure, cancellation grace-period, schema rollback | **NOT EXECUTED** |

The installed actionlint predates current queue syntax. `queue: max` is documented by
[GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).
Do not remove the shared queue to appease this local tool. Qualified lint is not a claim
that unfiltered lint passed.

Evidence directory: `artifacts/cicd-failure-task11/` (ignored, local).
Logs include `deploy-before-fix.log`, `deploy.log`, `runtime.log`, `readiness.log`,
`policy.log`, `workflow.log`, `vps.log`, `gateway.log`, raw/qualified lint results and
the source-hash/result manifest. Test counts above are groups/methods, not a claim of
18 complete end-to-end GitHub/Azure incident simulations.

## Exact rollback instructions — operator controlled; NOT EXECUTED

### 1. Establish incident ownership and retain evidence

Record run ID/attempt, validated SHA, failed phase, migration status, image digests and
previous/new revision names. Download the exact run/attempt's snapshot and k6 artifacts.
Confirm no current production/infrastructure/runtime/performance workflow or CLI operator
is mutating this environment. Do not dispatch a recovery workflow automatically.

From an already authorized Azure session, select the intended subscription, resource group
and affected app. These read-only commands collect only state/images/routing, not secrets:

```bash
export RESOURCE_GROUP=rg-researchtrack-prod
export INFRA_VM=vm-researchtrack-infra-prod
APP=rt-jira-prod                 # replace with the affected recorded app
az account show --query '{subscription:id,tenant:tenantId}' -o json
./deploy/azure/scripts/runtime-power.sh status
az containerapp show -g "$RESOURCE_GROUP" -n "$APP" \
  --query '{mode:properties.configuration.activeRevisionsMode,latest:properties.latestRevisionName,ready:properties.latestReadyRevisionName,traffic:properties.configuration.ingress.traffic}' -o json
az containerapp revision list -g "$RESOURCE_GROUP" -n "$APP" --all \
  --query '[].{revision:name,active:properties.active,health:properties.healthState,provisioning:properties.provisioningState,image:properties.template.containers[0].image}' -o json
```

Expected: correct subscription; known Single mode; captured previous revision still
exists at the reviewed digest. If it is already active/healthy and serving the correct
traffic, verify rather than recreate it. If identity/mode/traffic differs from the
incident evidence, stop and reconcile the difference before approving changes.

### 2. Recover Gateway exemption and exact power state if restoration failed

First inspect the snapshot paths and confirm they belong to this run/attempt and correct
resources. Run validation/verification before approving restoration. A snapshot is not
an ownership lease; do not replay an old snapshot over a newer release/operator change.

```bash
# Use the correct captured deployment OR smoke runtime snapshot, not both blindly.
RUNTIME_SNAPSHOT=/approved/incident/runtime-state.json
./deploy/azure/scripts/runtime-power.sh validate-snapshot "$RUNTIME_SNAPSHOT"
./deploy/azure/scripts/runtime-power.sh verify "$RUNTIME_SNAPSHOT"

# Only when an exemption snapshot exists for the affected session:
GATEWAY_SNAPSHOT=/approved/incident/gateway-state.json
./scripts/performance/gateway-exemption.sh verify "$GATEWAY_SNAPSHOT"
```

After separate operator authorization, restore the Gateway while runtime dependencies
remain available, then restore power state and verify. Gateway restore copies the
captured original template and removes only that session's temporary performance secret;
it is not a generic application-version rollback.

```bash
# MUTATING — only after ownership/current-state review and approval.
./scripts/performance/gateway-exemption.sh restore "$GATEWAY_SNAPSHOT"
./scripts/performance/gateway-exemption.sh verify "$GATEWAY_SNAPSHOT"
./deploy/azure/scripts/runtime-power.sh restore "$RUNTIME_SNAPSHOT"
./deploy/azure/scripts/runtime-power.sh verify "$RUNTIME_SNAPSHOT"
./deploy/azure/scripts/runtime-power.sh status
```

Skip Gateway restore when no exemption was enabled. Expected power state is exactly
the snapshot, possibly Stopped/Deallocated; powering down is not rollback of application
version. If additional incident work requires runtime to remain running, agree and capture
that temporary ownership separately, and restore the agreed original state afterward.

### 3. Review application/schema compatibility before a version rollback

Inspect **all applied migrations**, including services whose rollouts never started.
Confirm the selected previous image understands the current schema, Kafka topic/contract,
pending outbox events and durable receipts. Never run migration Down, restore an entire
production database automatically, reset consumer groups or clear pending records.
If compatibility is unknown, use a reviewed forward repair instead of guessing a rollback.
App-scope secrets/registry/ingress changes need separate review; copying a revision does
not restore their historical values.

For an approved **Single-mode** version rollback when the previous recorded revision is
not already serving, copy its entire template using the existing revision mechanism:

```bash
# MUTATING — only after compatibility, digest, ownership and rollback approval.
PREVIOUS_REVISION=rt-jira-prod--replace-with-recorded-revision
az containerapp revision show -g "$RESOURCE_GROUP" -n "$APP" \
  --revision "$PREVIOUS_REVISION" \
  --query '{active:properties.active,image:properties.template.containers[0].image,health:properties.healthState}' -o json
az containerapp revision copy -g "$RESOURCE_GROUP" -n "$APP" \
  --from-revision "$PREVIOUS_REVISION" -o none
```

This creates a new revision from the reviewed template; it does not redeploy an arbitrary
branch/image tag. See [Azure revision CLI](https://learn.microsoft.com/en-us/cli/azure/containerapp/revision?view=azure-cli-latest).
Then poll the read-only app/revision commands above with a bounded incident timeout
(e.g. 60 checks at 10-second intervals). Require the new latest revision to be latestReady,
Provisioned/Healthy, with the exact reviewed immutable digest and expected routing.
If it fails, retain the previous revision and diagnose; do not delete revisions or switch
to Multiple mode/alter traffic weights as an unreviewed workaround.

### 4. Verify recovery and close acceptance separately

Verify private networking, TLS and HTTP readiness, plus Kafka connectivity/metrics,
consumer offsets and durable backlog using approved read-only runbook commands. HTTP
200 with Degraded health is not proof of complete dependencies. Retain redacted state,
revision/digest, schema review, logs, readiness and monitoring evidence. A performance
failure needs a separate approved smoke rerun; skipped smoke remains pending. Provider
event/business acceptance requires the previously documented separate authorization.

## Reproduction and changed files

From this worktree, execute only offline suites:

```bash
python3 -m unittest discover -s deploy/azure/validation -p test_release_policy.py -v
python3 -m unittest discover -s deploy/azure/validation -p test_workflow_release_gates.py -v
./deploy/azure/validation/test-deploy-image-selection.sh
./deploy/azure/validation/test-runtime-power.sh
./deploy/azure/validation/test-runtime-readiness.sh
./tests/performance/test-run-vps.sh
./tests/performance/test-gateway-exemption.sh
actionlint
# Separate qualified result, not a substitute for reporting raw failures:
actionlint -ignore 'unexpected key "queue"' -ignore SC2129
```

Changed for TASK 11: `deploy/azure/scripts/deploy-container-apps.sh`,
`deploy/azure/scripts/release-summary.py`, `deploy/azure/validation/test-deploy-image-selection.sh`,
`deploy/azure/validation/test_release_policy.py`, `deploy/azure/validation/test-runtime-power.sh`,
`deploy/azure/validation/test-runtime-readiness.sh`, `tests/performance/test-gateway-exemption.sh`,
and this new report. The first seven are existing files; earlier task edits in them are preserved.
All work remains local: no commit, staging, push, merge, deployment, secret/settings change,
production restart or deliberate production failure.
