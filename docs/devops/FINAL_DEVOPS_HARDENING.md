# Final DevOps bug fixes and production readiness

Date: 2026-10-11. Repository: Nimsara-Jayarathna/researchtrack-backend.
Worktree: `researchtrack-backend-integration-main-kafka`.
Branch: `integration/main-kafka-production-20261011`.

## 1. Executive summary

Confirmed clean HEAD `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` before editing.
Fetched main `141181575e7b7fdda75e31356bfe9a980deadb7c`; integration 16 commits ahead,
zero behind. No uncommitted user changes existed. No branch overwrite/rebase occurred.

Infrastructure CD now locally requires the established exact-SHA unprivileged CI/Kafka
and trusted-source gate, retains Bicep validation, and rechecks SHA before production
credentials. ARM deployment failure remains fatal even when an existing revision is
healthy; revision read errors fail explicitly. Optional public readiness requires HTTP
200 with trusted TLS. Release reporting detects missing/failed readiness/restoration
metadata and preserves cancelled/failed/skipped smoke distinctions. Existing runtime
restoration and per-app rollback behavior are preserved and tested.

The application gate was already owner-published and is demonstrated in
[production 38147705025](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38147705025)
and [PR CI 38149500022](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38149500022)
at `a26cfd2`. Read-only job/log evidence confirms required CI/Kafka/source checks, seven
image builds/deployments, six migrations, infrastructure verification, restoration and
k6 passed. Public readiness was SKIPPED. New hardening changes are not published or run
in Actions. Branch/Environment protection and live Kafka acceptance remain incomplete.

## 2. Bug fix table

| Issue | Classification/root cause | Files | Resolution/test result |
|---|---|---|---|
| Infrastructure privileged job lacks full CI gate | Confirmed missing validation; only Bicep dependency | azure-infrastructure.yml, test_workflow_release_gates.py, backend-ci.yml | Reuse shared eligibility before Bicep, require both jobs, pin checkouts and recheck current SHA before credentials; offline PASS |
| Failed ARM reconciliation can become green | Confirmed bug; error logged, subsequent healthy old revision accepted | deploy-container-apps.sh, test-deploy-image-selection.sh | Preserve fatal ARM result, run existing rollback; explicit conditional-function CLI/JSON failures; PASS |
| Public redirect accepted as readiness | Confirmed bug; curl -f permits HTTP 3xx | backend-deploy-azure-production.yml, test_workflow_release_gates.py | Require exactly 200, preserve ten attempts/default TLS/GET/no credentials; PASS |
| Missing failure-path evidence/reporting | Validation gap; aggregate-only summary lacked restoration row and inconsistent-metadata rejection | release-summary.py, test_release_policy.py, test_workflow_release_gates.py | Failed/cancelled smoke, readiness, unrelated failures and restoration failures visible; incomplete required metadata cannot PASS; deterministic tests PASS |
| Branch/Environment unprotected | GitHub configuration issue, not fixed by workflow edits | GITHUB_ENVIRONMENTS.md, RUNBOOK.md | Exact owner UI/check-name/legacy-path checklist; settings NOT CHANGED |
| Jira issues latency | Investigation/handoff, no proven production root cause | This report and dated audit | Source trace + actual k6 + isolated EXPLAIN; no speculative application/index/threshold change |
| Kafka verification and image/tool maintenance | Production acceptance gap / optional maintenance | KAFKA_OPERATIONS.md, RUNBOOK.md, this report | Existing test suite rerun PASS, read-only/event procedures separated; no live restart/image update |
| Audit says gate unpublished | Historical documentation now superseded by current dated evidence | FINAL_DEVOPS_SPRINTS_1_TO_4_AUDIT.md and linked production reports | Preserve original account; add verified current run/SHA and acceptance status |

## 3. Exact changed files

| File | Reason |
|---|---|
| `.github/workflows/azure-infrastructure.yml` | Unprivileged reusable CI/source prerequisite, immutable checkouts, production SHA recheck |
| `.github/workflows/backend-ci.yml` | Run new offline infrastructure/public/restoration regressions in required CI |
| `.github/workflows/backend-deploy-azure-production.yml` | Require HTTP 200 for optional public readiness |
| `deploy/azure/scripts/deploy-container-apps.sh` | ARM failure cannot be hidden by revision health; explicit query/JSON failures |
| `deploy/azure/scripts/release-summary.py` | Required metadata completeness, failure evidence, restoration row and operator diagnostics |
| `deploy/azure/validation/test-deploy-image-selection.sh` | Failed ARM/healthy prior revision, revision API error, successful rollout regressions |
| `deploy/azure/validation/test_release_policy.py` | Realistic successful phase metadata and failed/cancelled/skipped/incomplete/restoration reporting cases |
| `deploy/azure/validation/test_workflow_release_gates.py` (new) | Workflow graph/credential order/source/Bicep/What-If/public GET/restoration shell regressions |
| `docs/devops/configuration/GITHUB_ENVIRONMENTS.md` | Exact ruleset/Environment UI, observed PR checks, legacy cutover and optional verification |
| `docs/devops/azure-production/RUNBOOK.md` | Infrastructure gate, failed-release decision path, snapshot/rollback diagnostics, deployed monitoring image capture |
| `docs/devops/azure-production/KAFKA_OPERATIONS.md` | Read-only final production checks and separately authorized provider-chain acceptance |
| `docs/devops/azure-production/04_PRODUCTION_CICD.md` | Correct CI trigger description, demonstrated application gate, local infrastructure gate |
| `docs/devops/azure-production/IMPLEMENTATION_STATUS.md` | Current SHA/comparison/run evidence versus remaining owner/live work |
| `docs/devops/azure-production/DEVOPS_4_4_REPORT.md` | Current five-test/gated deployment evidence while retaining historical failures |
| `docs/devops/FINAL_DEVOPS_SPRINTS_1_TO_4_AUDIT.md` | Dated current issue/story acceptance/evidence section, original audit retained |
| `docs/devops/FINAL_DEVOPS_HARDENING.md` (new) | Full change/test/security/performance/owner review record |

Ignored local evidence lives under `artifacts/final-devops-hardening/` and the disposable
run's `artifacts/kafka-integration/rt-kafka-fixture-f27fba9d9adf/`. Do not publish raw logs,
fixture credential/private-TLS files or unrestricted environment/config dumps.

## 4. CI/CD security and failure handling

Infrastructure: approved push/manual ref → shared Backend CI and required isolated Kafka
(upgrades/recovery) → trusted repo/ref/event/current SHA → Bicep build → production
Environment → exact-SHA recheck → existing secret materialization/OIDC → What-If →
reconciliation → post-verification. Required result must be exactly success; skipped,
failed, cancelled, missing, neutral and inconclusive CI cannot reach production. Wrong
branch/tag/PR/PR-target/fork and stale SHA are rejected by the same existing policy.

The eligibility caller has only contents-read, no inherited secrets, environment or
OIDC. Privileged infrastructure needs both eligibility and Bicep; no always/continue-on-
error bypass. Deployment checkouts pin github.sha and disable persisted credentials.
Existing manual monitoring_only/allow_destructive, shared queue/concurrency, path filters,
Bicep resources, OIDC, Environment, secret delivery and deletion gate remain. Monitoring
mode still uses existing outputs and monitoring-only reconciliation, with no infrastructure
replacement or broker/database/runtime-secret changes. Local graph depth is 4 for infra,
5 for application, acyclic and within GitHub's documented limit. Offline checks validate
graph/contracts/shell bodies, not an actual GitHub scheduler or Azure execution.

Application gating remains unchanged. Required deploy/smoke jobs have no continue-on-error;
a green reporting job cannot turn their failures into workflow success. Summary FAIL covers
failed deployment, k6 failure/cancellation/inconclusive result, missing required evidence,
readiness, restoration and unrelated step failure; optional skipped smoke reports final
acceptance pending. Optional public check remains explicit, not mandatory by default.
Public check is anonymous verified HTTPS GET, no redirects followed or credentials sent,
with at most ten 10-second attempts and six-second waits. It checks HTTP 200, not payload
or every business/Kafka dependency; existing Degraded/200 semantics are unchanged.

Restoration test bodies are extracted from real workflow steps and run against synthetic
runtime/Gateway script stubs. They prove owned restoration/verify, bounded fatal restore
or verify failure, ownership marker retention, unowned drift refusal, and fatal smoke
runtime/Gateway recovery failure. Existing runtime/session and Gateway wrapper tests prove
failed k6/session work still restores. No dangerous multi-service/schema rollback added.

## 5. Kafka and migration status

Preserved five real isolated tests: GitHub business/recovery flow, native producer outage
with durable acceptance, Jira business/recovery flow, GitHub historical upgrade and Jira
historical upgrade. Final run: GitHub 3/3, Jira 2/2, zero failures/skips. Actual TLS Kafka,
MySQL, producer/consumer/offset/replay, retries/outbox/receipt/business effects, HTTP health,
Prometheus scrape/metrics and alert fire/resolve are exercised. Provider clients are
synthetic. Application registry snapshots served to fixture Prometheus do not establish
current deployed ASP.NET /metrics. Both real historical migration chains preserve old
tables/records and verify new indexes/no-op reapply; no historical migration edited.

Fixture project `rt-kafka-fixture-f27fba9d9adf` is retained/stopped with its data/volume.
The isolated MySQL container alone was temporarily started for read-only EXPLAIN after
suite completion and stopped again; its exact project/name/volume and local socket were
verified. Production endpoints were never connected to a fixture. No volume/topic/message
was deleted, offset reset, table repaired or pending event cleared.

Existing mount verifier and diagnostic needed no rewrite: exact binds/read-only config
and only permitted image anonymous volumes remain enforced. Live assertion cause is
unconfirmed without actual mounts; proposed narrow correction is delivery of only the
already-reviewed verifier if the installed version is obsolete, after matching actual
mounts. No live mount change. See [operator acceptance](azure-production/KAFKA_OPERATIONS.md#final-production-acceptance--operator-controlled)
for broker/TLS/topics/mounts/identity/settings/groups/backlog/scrapes/Grafana/alerts and
separately approved GitHub/Jira webhook → outbox → Kafka → consumer/business → offset →
monitoring evidence. No production event injection/outage/restart is performed.
Single-broker RF1 remains an availability/disk-loss limitation.

## 6. Jira performance findings and maintenance

Exact route: authenticated `GET /api/v1/projects/{projectId:guid}/jira/issues` through
Gateway → ProjectJiraIssuesController.Issues → JiraIssueQueryService.GetIssuesAsync →
ProjectAuthorizationClient's Project Service GET authorization/access → JiraDbContext.
There is no separate repository layer in this path: EF queries MySQL directly.
One no-tracking unique project connection lookup, one project-filtered/IssueKey-ordered
projection including DescriptionJson, then in-memory status summary/JSON response.
No per-issue query loop, synchronous Atlassian HTTP or server-side read cache is present.
The endpoint returns the entire stored issue set without pagination. Current sync state
comes from the persisted connection; authorization and synchronization must be retained.

Real artifact 11705509313 from production run 38147705025:

| Measure | Actual evidence |
|---|---|
| Jira issues | 9/9 HTTP 2xx, 0% failure; avg 1921.51 ms; p95 3076.93 ms |
| GitHub activity | 8/8 HTTP 2xx; p95 375.95 ms |
| Business overall | 61 requests, 0% failure; p95 1566.86 ms |
| Applied overall threshold | 3000 ms from K6_SMOKE_P95_MS |
| Endpoint map | `{}`; Jira target/threshold outcome absent |
| Earlier Jira sample | Run 38135611889: 8 requests, p95 3755.43 ms (historical audit) |

Isolated equivalent-query EXPLAIN (not EXPLAIN ANALYZE/production): connection lookup
uses `IX_jira_connections_ResearchProjectId`, const access; issues use
`IX_jira_issues_ResearchProjectId_IssueKey`, ref access, no filesort, one synthetic row.
No missing index is demonstrated. Tiny fixture plans cannot exclude a different plan,
large result/DescriptionJson serialization cost, auth-hop/connection latency, cold start,
VM/database pressure or production data skew. No application/query/index/migration/cache/
response contract/profile/threshold change was made.

Developer handoff: instrument authorization, connection lookup, issue query, summary and
serialization separately on an approved test dataset; record row count, response bytes,
DescriptionJson size, cold/warm/concurrency distribution and actual EF generated SQL/plan.
Compare realistic datasets and several representative runs; then agree a target and, if
justified, configure `K6_ENDPOINT_P95_THRESHOLDS_JSON` for operation `jira_issues`. Projection,
pagination or caching require contract/freshness/auth review and application regression
coverage. Do not treat aggregate smoke success as proof of a Jira endpoint SLO.

Actual Node20→forced-Node24 warnings name checkout@v4, setup-dotnet@v4, setup-node@v4,
upload-artifact@v4, azure/login@v2, docker/login-action@v3, build-push-action@v6 and
setup-buildx-action@v3. Existing actions succeeded in the confirmed run. Official current
release manifests checked read-only show Node24 candidates: checkout v7.0.1, setup-dotnet
v6.0.0, setup-node v7.1.0, upload-artifact v7.0.2, Azure/login v3.1.0, docker/login-action
v4.6.0, build-push-action v7.4.0, setup-buildx-action v4.4.1. These are review candidates,
not tested drop-in upgrades. No action major was changed. Review release notes/runner
requirements/cache/artifact/OIDC/build provenance semantics and test unprivileged CI
before separately approved production compatibility checks. Evidence:
`action-version-review.json` with exact official manifest links;
[checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet),
[Azure login](https://github.com/Azure/login), [Docker login](https://github.com/docker/login-action).

Raw actionlint 1.7.12 has ten baseline findings: five unsupported `concurrency.queue:max`,
five SC2129 redirect-style suggestions. Existing successful Actions execute queue max;
retain it. Qualified lint excludes only those known diagnostics, not trust/security rules.
Raw changed-shell ShellCheck reports baseline SC1091 (source not followed); source-aware
lint reports SC2153 (RESOURCE_GROUP versus a sourced local resource_group). Both are
informational; warning/error severity passes. RESOURCE_GROUP is a validated required
environment variable, so no variable rename or check suppression was introduced. Node's performance report test emits the existing
package module-type warning while passing; no unrelated package configuration edited.
GitHub annotations also warn ubuntu-latest will move to Ubuntu 26 beginning October 19;
review disposable fixture/native dependency/CLI compatibility before that transition.
Azure Container Apps extension behavior warnings are informational in successful historical
runs; no confirmed root cause. CLI/extension updates require operator compatibility evidence.

Prometheus/Grafana still declare latest. No deployed image digest/version was obtained;
no guess, upgrade/downgrade or pull/recreate performed. The runbook supplies selective
read-only image ID/RepoDigest/version capture; pin exactly the verified compatible deployed
version/digest after storage/backup compatibility and owner review.

## 7. Actual executed tests and limitations

Commands were run from the integration worktree. `bash` below means Homebrew Bash 5 on
macOS (`/opt/homebrew/bin/bash`). Evidence directory: `artifacts/final-devops-hardening/`.

| Command | Result | Evidence |
|---|---|---|
| `dotnet restore ResearchTrack.sln --verbosity minimal` | PASS | restore.log |
| `dotnet build ResearchTrack.sln -c Release --no-restore --verbosity minimal` | PASS, 0 warnings/errors | build.log |
| `COVERAGE_MIN_BUSINESS_LINE=65 COVERAGE_MIN_BUSINESS_BRANCH=50 bash scripts/coverage.sh --no-build` | PASS, 825 tests, 5 conditional SKIPPED; 78.30/64.50% business coverage | coverage.log; artifacts/coverage |
| `dotnet test tests/ResearchTrack.GitHubService.Tests -c Release --no-build --filter 'FullyQualifiedName~Kafka&Category!=KafkaIntegration'` | PASS, 44, no skips | github-kafka-unit.log/TRX |
| Same filter, `tests/ResearchTrack.JiraService.Tests` | PASS, 11, no skips | jira-kafka-unit.log/TRX |
| `bash scripts/test-kafka-integration.sh` | PASS, GitHub 3/Jira 2, zero skips; real upgrades/recovery/monitoring | kafka-integration.log; kafka-trx-results.json; fixture directory |
| `python3 deploy/azure/validation/test_release_policy.py` | PASS, 17 | release-policy.log |
| `python3 deploy/azure/validation/test_workflow_release_gates.py` | PASS, 16, with subcases | workflow-gates-public-final.log |
| `python3 deploy/azure/validation/test_monitoring_reconciliation.py` | PASS, 12 | monitoring-reconciliation.log |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS, 16 cases | test-deploy-image-selection-final.log |
| `python3 deploy/azure/validation/test_kafka_topics_config.py` | PASS, 21 | kafka-topic-config.log |
| `bash scripts/test-kafka-observability.sh` | PASS, 159 config /30 scenarios /55 expressions | kafka-observability.log |
| `bash deploy/azure/validation/test-kafka-operations.sh` | PASS, 50 shell +8 Python cases | test-kafka-operations.log |
| `bash deploy/azure/validation/test-validate-azure-env-files.sh` | PASS, 22 cases | same-name log |
| `bash deploy/azure/validation/test-service-env-composition.sh` | PASS, 10 cases | same-name log |
| `bash deploy/azure/validation/test-kafka-smoke.sh` | PASS, 9 | same-name log |
| `bash deploy/azure/validation/test-network-probe.sh` | PASS, 12 | same-name log |
| `bash deploy/azure/validation/test-preflight.sh` | PASS, 5 | same-name log |
| `bash deploy/azure/validation/test-runtime-power.sh` | PASS, 11 | same-name log |
| `bash deploy/azure/validation/test-runtime-readiness.sh` | PASS, 4 | same-name log |
| `bash deploy/azure/validation/test-private-ip-check.sh` | PASS, 10 | same-name log |
| `bash deploy/validate-shared-auth-wiring.sh` | PASS | shared-jwt.log |
| `bash tests/performance/{validate,test-report,test-run-vps,test-discover-vps-ip,test-gateway-exemption}.sh` (each individually) | PASS, validators/synthetic/mocked orchestration | performance-*.log |
| Native Bicep 0.48.1 `build deploy/azure/main.bicep --stdout` and `build deploy/azure/modules/container-app.bicep --stdout` | PASS, both; existing SHA-verified official binary | bicep-main.log, bicep-app.log; prior bicep-results.json |
| `actionlint -no-color` | FAIL, ten baseline queue/style findings | actionlint-raw.log |
| `actionlint -no-color -ignore 'unexpected key "queue" for "concurrency" section' -ignore 'SC2129:'` | Qualified PASS | actionlint-qualified.log |
| `shellcheck deploy/azure/scripts/deploy-container-apps.sh deploy/azure/validation/test-deploy-image-selection.sh` | Raw FAIL: baseline informational SC1091; source-aware FAIL: informational SC2153; `--severity=warning` PASS | shellcheck-results.json; shellcheck-{raw,source-aware,warning-errors}.log |
| `bash -n` for repository shell files; Python compile | PASS, 87 shell files /15 Python files | shell-syntax.json, python-syntax.json |
| Real workflow What-If script with stub Azure | PASS: no deletion, deletion blocked, explicit override preserved, CLI failure blocked | Included in 16 workflow gate tests |
| Read-only equivalent Jira EXPLAIN on owned disposable MySQL | PASS; index findings above, no latency claim | jira-isolated-explain.json |
| New summary rendered against real successful run metadata | PASS; preserves public SKIP and seven builds | latest-run-summary-replay.md |
| `python3 deploy/build/service-impact.py impact --head HEAD --base origin/main` | PASS, read-only CLI | service-impact-readonly.log |
| `git diff --check` | PASS | diff-check.log |

Not executed here: new Actions infrastructure gate/production rejection demonstration,
Azure live inventory/settings/mount checks, live provider event acceptance, real new k6
run/action-major compatibility, other dedicated `Category=DatabaseIntegration` suites.
No Azure CLI/login or authorized full-stack test account/dataset was configured. The full
synthetic Git-history `deploy/build/test_service_impact.py` harness creates fixture commits;
it was not rerun under the strict no-commit instruction. Its unchanged version passed in
verified baseline CI; the read-only impact command is not an equivalent substitute.
The ordinary suite's five conditional skips are not counted as passes; all five executed
separately in the required fixture. Local mocks/EXPLAIN never prove live production behavior.

### Reproduction on macOS

Use Docker Desktop with a local Unix socket, .NET 10 matching global.json, Python 3,
OpenSSL, jq, Node, Homebrew Bash 5, actionlint and ShellCheck. Run the commands above;
restore/build before coverage; run the isolated runner separately. It refuses occupied
127.0.0.1 ports 19092/13307/19090 and remote Docker, generates disposable TLS credentials,
uses a unique project and stops only its own containers while retaining data. Do not free
ports by stopping unrelated containers or run down -v/prune. Follow
[KAFKA_INTEGRATION_TESTING.md](azure-production/KAFKA_INTEGRATION_TESTING.md#devops-44-reproducible-recovery-smoke-on-macos)
for detailed existing smoke/evidence instructions. Ordinary five skips do not satisfy the
fixture requirement; inspect GitHub/Jira TRX and final no-skips assertion.

## 8. GitHub settings the owner must change

Follow the [exact UI checklist](configuration/GITHUB_ENVIRONMENTS.md#owner-protection-checklist--verified-2026-10-11).
Settings → Rules → Rulesets → New branch ruleset → Active → exact integration branch;
PR review, up-to-date approved checks, block force push/deletion and tightly scoped bypass.
Observed PR names (GitHub Actions expected source): `Restore, Build and Test` and
`Required isolated Kafka recovery and migration upgrades / kafka-integration`.
Do not require production job names on PRs. Settings → Environments → production →
Selected branches and tags → exact Branch integration/main-kafka-production-20261011;
independent reviewers/prevent self-review/admin bypass restrictions where supported.
Preserve Environment secrets and OIDC. Protected branches only would also admit main/develop.
Other refs still hold older workflow copies; inventory their Environment/OIDC/repository-
secret access and retire production paths through separately reviewed changes. No settings
or other branch was automatically modified.

## 9. Production verification still required

Owner-applied effective protections and reviewed publication/first infrastructure-gated
run; optional public URL/contract/flag verification; actual Azure image/revision/schema/
backup/mount/cluster/CA/NSG inventory; deployed GitHub/Jira metrics and approved provider
chains, groups/backlog/offsets; Grafana authenticated datasource/panels/alerts; reviewed
runtime/revision rollback demonstration; representative Jira latency measurements; deployed
monitoring digest/version capture and candidate action/runner compatibility.

## 10. Final safety review and Definition of Done

- [x] Initial status/HEAD/main/recent workflows/docs/policy inspected; existing work preserved.
- [x] Application gate demonstrated from exact baseline run; new infrastructure gate local.
- [x] No untrusted artifacts, privileged CI secrets, bypass for skipped CI or circular workflow.
- [x] Bicep/What-If/monitoring-only/manual inputs/shared lock/OIDC/Environment retained.
- [x] ARM/public/summary failure corrections with deterministic regression evidence.
- [x] Fresh build/unit/coverage/Kafka/upgrades/observability/operations/runtime/performance checks.
- [x] Exact changed files, warnings, skips and nonexecuted checks documented.
- [x] Existing app source/contracts/historical migrations/Kafka/MySQL persistent configuration unchanged.
- [x] No commit/push/merge/PR/deployment/workflow trigger/production settings or data mutation.
- [ ] Owner protection, publication/new infrastructure Actions demonstration and live acceptance.
- [ ] Representative Jira target, monitoring pinning and action/runner upgrade verification.

Runtime snapshots and previous healthy revisions remain the recovery evidence. Per-app
rollback is retained; cross-service/schema rollback needs compatibility review and separate
authorization. Additive Kafka tables, durable outbox/inbox, topics/offsets/cluster/CA and
all persistent data must survive recovery. New local changes are ready for review;
production acceptance is not declared complete.

**READY FOR USER CODE REVIEW — NO COMMIT OR PUSH PERFORMED**
