# Final DevOps Sprints 1–4 audit

## Current acceptance audit — TASK 14, 2026-10-12

**BLOCKED — ACCEPTANCE EVIDENCE MISSING.** The local implementation is ready for
review, but full Sprint 1–4 acceptance is not established. This dated section and
the [final acceptance checklist](FINAL_DEVOPS_ACCEPTANCE_CHECKLIST.md) supersede the
earlier findings preserved below. Existing reports remain evidence of their stated
source/version and test scope; their old recommendations are not current instructions.

Audit source: `integration/main-kafka-production-20261011`, local HEAD
`1810f09535d6a6d00947bcf84d18fde473e40c39` plus uncommitted TASK 02–13 work.
Main is the permanent default; integration is a temporary production-testing source.
No permanent integration ruleset is recommended. No deployment source was switched,
Jira status edited, cloud/GitHub setting changed, workflow dispatched or code published.

### Evidence classification

| Classification | Meaning in this audit |
|---|---|
| IMPLEMENTED | Inspected source/configuration exists; does not assert runtime behavior |
| TESTED LOCALLY | Executed local evidence with source applicability and limitations recorded |
| VERIFIED IN CI | Exact GitHub run/job/SHA evidence, covering that published version only |
| DEPLOYED | Deployment evidence identifies the release/configuration applied or retained at that time |
| VERIFIED IN PRODUCTION | Specific authorized live behavior/evidence; never inferred for all dependencies from a green workflow |
| PENDING APPROVAL | Proposed provider activity, live change or cutover requires operator approval; not executed |
| NOT VERIFIED | Required evidence unavailable, skipped, failed, outdated for changed source or outside tested scope |

A requirement can have multiple evidence stages. Jira Done/checkmarks are administrative
state, not new acceptance proof. Conditional skipped tests are reported as skipped.

### Branch, application preservation and PR audit

Comparison snapshot recorded at **2026-10-12 08:15:14 UTC** after read-only fetches.

| Item | Latest observed evidence |
|---|---|
| Integration local/remote HEAD | `1810f09535d6a6d00947bcf84d18fde473e40c39` |
| Main fetched HEAD | `c79789d977d733aeb3ed2d247e0b4c746f300cbc` |
| Merge base | `141181575e7b7fdda75e31356bfe9a980deadb7c` |
| Ahead/behind | Integration **17 ahead / 14 behind** main; `git rev-list --left-right --count origin/main...HEAD` = `14 17` |
| Open integration → main PR | None returned by read-only all-state head-filtered query |
| Historical PRs | [#84](https://github.com/Nimsara-Jayarathna/researchtrack-backend/pull/84) and [#83](https://github.com/Nimsara-Jayarathna/researchtrack-backend/pull/83) closed, `merged_at=null`; neither represents acceptance of this local diff |
| Main protection | Protected; existing ruleset retains deletion/force-push prevention, two approvals, Code Owner review, resolved conversations, strict `Restore, Build and Test` and required `test` deployment |
| Temporary integration | Remote protected=false; local app/infra release filters and release policy explicitly select integration |
| Production Environment | `production`, `protection_rules=[]`, `deployment_branch_policy=null`; not modified |

Main moved from `60b7edd` to `c79789d` during inspection. The frozen comparison above
is the final fetched snapshot, not a promise that remote branches stop advancing.
The new main commits add code-quality/Sonar workflows and coverage, formatting and
runtime security changes. **339 files** differ from the merge base on main; seven
overlap committed Kafka integration changes, and two overlap local modifications.
These are potential integration conflicts, not a claim that a merge was attempted.
Full names/commits/timestamps are in `artifacts/final-devops-task14/branch-comparison.json`.

Auth cookie validation/default security, random dummy password timing material, health
response cancellation, async Auth/Gateway host lifetime and quality tooling are among
the new main changes. They are not yet incorporated here. Consequently, the earlier
statement that integration preserves **latest** main is superseded. Existing integration
Auth, Project, Meeting, Submission, Gateway, GitHub and Jira regression behavior remains
tested locally; compatibility with the new main implementation is **NOT VERIFIED**.
No merge, branch overwrite or speculative conflict resolution occurred in TASK 14.

### Current deployment routes and live configuration concerns

The integration copy of `backend-deploy-production.yml` pushes integration only and
calls `backend-deploy-azure-production.yml`; infrastructure and release policy use the
same temporary source. Both retain the existing shared `researchtrack-azure-production`
concurrency, production Environment, OIDC, runtime restoration and rollback logic.

**Confirmed cross-branch route conflict remains:** the fetched **main** copy still
automatically starts the same Azure production workflow on main pushes and lacks the
integration copy's same-SHA required-CI gate. Latest main production runs were cancelled,
not accepted releases. A cancelled run does not disable the route or prove future main
deployments cannot overwrite Kafka images. Main quality/Sonar success is not a Kafka
release gate. This audit changes no triggers. Coordinate any temporary route control or
final cutover separately with the owner; do not edit old branches automatically.

GitHub reports immutable OIDC subject prefix
`repo:Nimsara-Jayarathna@66525584/researchtrack-backend@1335622081`, default subject
enabled. Production Environment jobs use an Environment subject; it is not a branch
attestation. Azure federated issuer/subject/audience and effective roles remain
**NOT VERIFIED**. Historical successful login demonstrates exchange at that time only.
The older name-only bootstrap assumption must not be used to replace the existing
identity/federation. Read-only instructions are in [CI/CD security verification](CICD_SECURITY_VERIFICATION.md).

Main's active rules are preserved. Environment restrictions/reviewer options are an
owner decision, not a requirement to permanently protect this temporary branch.
Legacy main/DevOps branch routes and authorized Test/manual operations need coordinated
review, as described in [legacy deployment audit](LEGACY_DEPLOYMENT_WORKFLOW_AUDIT.md).

### Exact GitHub CI/CD evidence

Read-only evidence under `artifacts/final-devops-task14/` includes run/job/step/artifact
metadata and selected count/provenance lines only; raw privileged logs were not retained.

| Run / source | Actual result and evidence scope |
|---|---|
| [Application production 38153663471](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663471), `1810f09535d6a6d00947bcf84d18fde473e40c39` | SUCCESS. Required CI job `114512094615`, isolated job `114512094836`, trusted-source job and deployment job succeed. Unit/mock **825 pass / 5 conditional skips**; separate real Kafka **GitHub 3 + Jira 2 pass, zero skipped**. Observability **159 / 30 / 55**. Public readiness **SKIPPED**. Runtime restoration, automatic k6 smoke and final safeguards succeed. |
| [Infrastructure 38153663507](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663507), same `1810f095` full SHA above | SUCCESS. Required ordinary CI and isolated Kafka jobs, source eligibility, Bicep validation/What-If, OIDC, reconcile and VM/private-network validation steps succeed. Evidence applies to published infrastructure at that execution. |
| [Earlier production 38147705025](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38147705025), `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` | Historical successful image/migration/rollout baseline; details retained below. It does not validate later local fixes. |
| [PR CI 38149500022](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38149500022), `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` | Historical successful PR CI; no current open integration PR. |
| [Main production 38221614969](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38221614969), `c79789d977d733aeb3ed2d247e0b4c746f300cbc` | CANCELLED; not deployment acceptance. |
| [Main quality 38221614413](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38221614413), same `c79789d` full SHA above | CANCELLED; not a successful required check. |
| [Main Sonar 38221614464](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38221614464), same `c79789d` full SHA above | SUCCESS for that separate analysis workflow; not validation of this integration diff/Kafka release. |

The latest integration application release was a **no-op for images**: build matrix was
skipped for unchanged sources; required CI and Kafka were not skipped. Seven retained
image digests have OCI source `a26cfd2b2b20`, captured in selected deployment evidence
`114513979634-selected-log-evidence.json`. Do not describe it as newly building or
migrating all services at `1810f095`. Current Azure revision/digest/schema/state inventory
was not queried; no present-tense deployed equivalence with local TASK 02–13 is claimed.

Available artifact IDs for application run: isolated Kafka **11708335296**, unit/mock
coverage **11708030294**, k6 smoke **11708570810**, runtime before deploy **11707732458**,
k6 runtime state **11708266175**. Infrastructure isolated/coverage artifacts:
**11707529398 / 11708311799**. API metadata reported unexpired at inspection; availability
can change. No workflow was triggered by this audit.

### Fresh local validation and retained evidence applicability

| Validation | Actual outcome / source scope |
|---|---|
| TASK 14 Release solution build | PASS, zero warnings/errors; `artifacts/final-devops-task14/build.log` |
| TASK 14 ordinary unit/mock coverage | **849 PASS, 5 SKIPPED**, zero failures: Gateway 28, Auth 129, Project 130, Meeting 51, Submission 116, GitHub 289 + 3 skips, Jira 106 + 2 skips. Business coverage **78.3% line / 64.7% branch**, original floors unchanged. `coverage.log`, `coverage-summary.md`, `results.json`. |
| TASK 14 offline Azure Python regression discovery | **98 PASS**, zero skips: workflow 34, release policy 25, monitoring reconcile 12, topics/config 21, backup 6. `python-regressions.log`. |
| TASK 13 backup publication/mount/rotation safety | Six PASS after four reproduced pre-fix failures; source and logs in [DR plan](DISASTER_RECOVERY_PLAN.md) |
| TASK 13 real MySQL synthetic restore | PASS six databases, 6 business rows, 24 outbox rows, 6 receipts, precision/objects/unique constraints; network none, all fixture volumes retained. Not full application/Kafka restore. |
| TASK 13 existing Kafka safety and messaging subsets | PASS 62 shell operational cases + 8 Python methods; GitHub/Jira messaging 15/15 zero skipped. Initial parallel-build contention failed before GitHub tests; sequential rerun passed. |
| Earlier TASK 09 real Kafka/MySQL suite | **5 PASS / zero skipped**, fixture `rt-kafka-fixture-cfa1267c500d`. All recorded fixture/flow/worker/persistence source hashes match current files. Not rerun in TASK 14. |
| Earlier TASK 08/12 observability | **159 configuration checks / 33 rule scenarios / 55 dashboard expressions PASS**, plus nine metric tests and 21 operator expressions; explicit original evidence. Baseline CI ran 30 scenarios, not these 33. |
| Earlier TASK 11 failure handling | PASS policy/workflow/deployer/runtime/readiness/Gateway/VPS synthetic cases; source hashes match. Raw/qualified tooling distinctions retained. |
| Earlier TASK 12 workflow tooling | Raw actionlint **FAIL: five existing `queue` schema findings**; qualified check ignoring only that key PASS. Workflow ShellCheck warning/error severity PASS. It is not a clean raw lint pass. Node24 setup-dotnet upgrade is local, actual Actions execution not verified. |
| General protected DatabaseIntegration suites / full production restore / live provider acceptance | NOT EXECUTED in TASK 14; do not equate with five Kafka fixture tests |

`historical-source-applicability.json` compares retained TASK 08/09/11/12 file hashes with
current source; all recorded files match. Those hashes support the stated test scope,
not entire-worktree equivalence. Coverage evidence is from this local diff, while GitHub
evidence is from published commits. Repeated counts above are not summed into a new
single test total. Existing operational tests use stubs; the five fixture tests use real
Kafka/MySQL with provider responses stubbed.

### Component acceptance and production risks

| Component | Current evidence stages | Remaining requirement |
|---|---|---|
| GitHub CI / trusted Azure CD | IMPLEMENTED, TESTED LOCALLY; published gate VERIFIED IN CI | Publish reviewed local fixes through normal owner process, get exact-SHA evidence; resolve active competing main route separately |
| Docker/GHCR / Azure app revisions | Baseline DEPLOYED with digests; local composition/provenance TESTED LOCALLY | Current image/revision/schema inventory and seven-service routing acceptance; latest main changes not integrated |
| Azure infrastructure / private networking | IMPLEMENTED, published workflow verified; effective cloud state NOT VERIFIED | Read-only NSG/listener/disk/ACA/DNS/TLS evidence and Test/frontend preservation |
| Security/OIDC | Source authorization/secret handling TESTED LOCALLY; historical login succeeds | Actual federation/RBAC inventory and all active trusted deployment routes; no identity/config changes here |
| Kafka infrastructure | IMPLEMENTED, isolated restart/TLS safety TESTED LOCALLY and baseline VERIFIED IN CI | Current broker mounts/identity/CA/retention/private connectivity/exporter evidence |
| GitHub/Jira flows | IMPLEMENTED, real disposable business DB/Kafka TESTED LOCALLY; baseline VERIFIED IN CI | Authentic designated provider events and resulting receipts/jobs/business rows/offset/metrics evidence PENDING APPROVAL |
| MySQL upgrades | Additive migrations and historical upgrades in five-test fixtures PASS | Current applied schema histories and runtime compatibility; representative backup restore including actual application tables |
| Prometheus/Grafana/alerts | Config/rules/dashboard queries TESTED LOCALLY; baseline provisioning/validation deployed | Actual app metric scrapes, authenticated panels, datasource and bounded live observations; controlled failure demonstrations only outside production |
| k6 / Jira API | Historical production smoke succeeds; synthetic multi-run measurements TESTED LOCALLY | Representative production-stage latency diagnosis and owner-approved endpoint SLO; no threshold weakening |
| Rollback / restoration | Synthetic failures TESTED LOCALLY, baseline runtime restoration succeeds | Owner-approved compatible revision recovery evidence; no destructive schema rollback or blanket Kafka repair |
| Backup/DR | Publication fixes and six-DB synthetic drill TESTED LOCALLY | Independent off-VM coherent backup, freshness/decryption proof, application/Kafka restore, measured RTO/RPO NOT VERIFIED |
| Technical docs | TASK 02–13 reports, runbooks, operator procedures and this checklist IMPLEMENTED | Owner evidence/sign-off; historical recommendations must not be applied as current instructions |

Public gate is anonymous GET `https://api.researchtrack.blipzo.xyz/health/ready` unless
an approved hostname variable overrides it. It requires exactly 200, verified TLS,
no redirect following, bounded ten requests/10s per request/6s sleeps. Repository and
production Environment GETs for `PRODUCTION_VERIFY_PUBLIC_ENDPOINT` returned 404; the
actual baseline step is skipped. No flag was enabled. HTTP 200 can include Degraded;
Gateway readiness has no downstream dependency checks. Follow
[public readiness verification](PUBLIC_API_READINESS_VERIFICATION.md) to verify DNS,
certificate and response contract before separately authorizing the flag change.

Kafka flow proves durable handoff **before offset commit**, not that business sync
must finish before commit. Jira directly schedules durable sync at ingress too.
After commit failure, real replay/dedup succeeds. TASK 09 verifies 128 acknowledged
duplicate GitHub records survive ordinary restart with low/high 0/130, group offset 2
unchanged, then exact records replay and commit 130 with one receipt/sync run. This is
not arbitrary distinct-event batch, power-loss, multi-replica or disk-loss proof.
All remaining narrower scenarios are listed in [recovery report](KAFKA_FAILURE_RECOVERY_FINAL_REPORT.md).
No offsets, pending rows or topics were reset/deleted to pass tests.

Prometheus rules cover all ten Kafka alerts and recovery in deterministic tests. Real
isolated faults demonstrate selected broker/exporter and held/lag/processing changes;
all ten real fault types and rolling-window expiry were not induced. Metrics use bounded
labels; absence of sensitive values in current deployed metrics remains unverified.
Use the read-only queries in [observability final report](KAFKA_OBSERVABILITY_FINAL_REPORT.md).

Historical small production Jira Issues samples around 2.7–3.8s p95 do not identify
Kafka as the cause. TASK 10 ran two safe synthetic invocations, **8,400 timed requests**,
three runs per data-size/concurrency scenario: 100-issue serial p95 9.779–10.534ms;
5,000-issue/concurrency-four p95 499.846–746.990ms, about 12.28MB responses. No Kafka
workers participate in that benchmark. External authorization delay/large payloads
affect the isolated path, but production root cause remains **NOT VERIFIED**. No caching,
index or threshold relaxation was applied. See [performance investigation](JIRA_ISSUES_PERFORMANCE_INVESTIGATION.md).

Single-node KRaft/RF1 and same-disk SQL backups remain critical DR limitations. Existing
local backup retention does not imply off-site protection. The [DR plan](DISASTER_RECOVERY_PLAN.md)
describes matched DB/Kafka recovery hazards, operator approval, costs and measured-test
limits. No standby/vault/storage account was provisioned.

### Authoritative story requirements and story-by-story findings

Read-only Jira on `researchtrack.atlassian.net`, epic
[SCRUM-198](https://researchtrack.atlassian.net/browse/SCRUM-198), returned the twelve
known Sprint 2–4 stories, their descriptions and Done state. Full scope/verification/DoD
is retained without assignee/contact fields in `artifacts/final-devops-task14/jira-requirements.json`.
The checklist enumerates each story's actual verification criteria, separately from
administrative ticket status.

**Sprint 1 NOT VERIFIED:** cross-system search, epic/direct DevOps/deployment queries
and targeted Sprint 1/DO-1 searches found no authoritative Sprint 1 DevOps story mapping.
Confluence search returned **403, current user not permitted**; cross-system search
warned of incomplete sources. Related application stories mentioning Sprint 1 are not
DevOps acceptance specifications. No Sprint 1 IDs were invented. The owner must supply
the actual story source/criteria or grant read-only access to the missing source.

The preserved technical baseline—container builds, GHCR, VPS Test, shared JWT/env wiring,
service databases/migrations and seven service regression suites—is IMPLEMENTED and
TESTED LOCALLY within the limits above. Ticket-level Sprint 1 acceptance stays blocked.

#### Sprint 2 — [SCRUM-194: DO-201-Integrate Prometheus Metrics Collection](https://researchtrack.atlassian.net/browse/SCRUM-194)

- **Scope and acceptance:** Private Gateway/service scraping; useful request/availability metrics; monitoring independence and privacy. The exact 7 original verification criteria are enumerated under `SCRUM-194` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/monitoring/prometheus.yml`, `deploy/azure/vm/prometheus/prometheus.yml.template`, `src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/Extensions/ServiceCollectionExtensions.cs`.
- **Automated / real integration evidence:** Configuration validator and promtool; TASK 09 real fixture scrape/selected transport outage. CI job 114512094615 validates the published configuration. Full Sprint 2 Test target accumulation and application use with Prometheus stopped are not verified.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Actual configured Test targets UP, accumulating metrics and approved Test service/Prometheus outage-recovery demonstration.

#### Sprint 2 — [SCRUM-195: DO-202-Deploy Grafana and Build ResearchTrack Service Dashboard](https://researchtrack.atlassian.net/browse/SCRUM-195)

- **Scope and acceptance:** Persistent Grafana/Prometheus provisioning, overview health/request/error/latency and initial GitHub panels. The exact 6 original verification criteria are enumerated under `SCRUM-195` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/monitoring/grafana/provisioning/datasources/prometheus.yml`, `deploy/monitoring/grafana/provisioning/dashboards/json/researchtrack-overview.json`, `deploy/azure/vm/compose.yml`.
- **Automated / real integration evidence:** Dashboard JSON/query parsing and provisioning tests; CI monitoring verifier succeeds. No authenticated rendered Grafana UI/datasource/traffic demonstration was collected.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Rendered overview, datasource health, populated request/error/latency panels, persisted configuration and visual Test outage/recovery.

#### Sprint 2 — [SCRUM-196: DO-203-Add GitHub Integration Operational Monitoring](https://researchtrack.atlassian.net/browse/SCRUM-196)

- **Scope and acceptance:** GitHub synchronization, webhook, approximately daily reconciliation, duration and low-cardinality private monitoring. The exact 6 original verification criteria are enumerated under `SCRUM-196` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubWebhookMetrics.cs`, `src/Services/ResearchTrack.GitHubService/Features/Synchronization/GitHubReconciliationMetrics.cs`, `deploy/monitoring/grafana/provisioning/dashboards/json/github-sync.json`.
- **Automated / real integration evidence:** GitHub regression 289 fresh passes, metric tests and real disposable business/receipt flow. Baseline CI GitHub isolated 3 passes. Provider HTTP is stubbed; no real daily reconciliation/panel acceptance.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Deployed success/failure/webhook/reconciliation metrics and panels, privacy review and approved controlled non-production integration failure.

#### Sprint 2 — [SCRUM-197: DO-204-Configure Basic Monitoring Alerts and Failure Verification](https://researchtrack.atlassian.net/browse/SCRUM-197)

- **Scope and acceptance:** Meaningful service/Gateway/error/GitHub/reconciliation alerts and recovery; no sensitive content. The exact 5 original verification criteria are enumerated under `SCRUM-197` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/monitoring/rules/researchtrack-alerts.yml`, `deploy/monitoring/tests/kafka-alerts.test.yml`, `deploy/monitoring/tests/validate_kafka_observability.py`.
- **Automated / real integration evidence:** Rule tests preserve existing alert rules; local 33 scenarios pass, published CI has 30. Selected real fixture Kafka/processing alerts are not a complete Sprint 2 Test outage demonstration.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Test service-unavailable and GitHub failure alert firing/resolution, with redacted alert content and operational timestamps.

#### Sprint 3 — [SCRUM-204: DO-301-Extend Prometheus and Grafana for Jira Integration Monitoring](https://researchtrack.atlassian.net/browse/SCRUM-204)

- **Scope and acceptance:** Reuse monitoring foundation for Jira health/webhook/sync/reconciliation/failures and private bounded labels. The exact 6 original verification criteria are enumerated under `SCRUM-204` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `src/Services/ResearchTrack.JiraService/Features/JiraOperationalMetrics.cs`, `deploy/monitoring/grafana/provisioning/dashboards/json/jira-integration.json`, `deploy/monitoring/rules/researchtrack-alerts.yml`.
- **Automated / real integration evidence:** Jira regression 106 fresh passes, dashboard/rule checks and two real disposable Kafka/MySQL flows with business state. Provider HTTP is stubbed; no authenticated deployed Jira panels.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Live Jira health/webhook/reconciliation/sync panels, controlled non-production failure/recovery, and metric privacy.

#### Sprint 3 — [SCRUM-205: DO-302-Provision Azure Production Infrastructure and Container Apps Environment](https://researchtrack.atlassian.net/browse/SCRUM-205)

- **Scope and acceptance:** Azure resource group/ACA/private networking/ingress/DNS/TLS/storage/configuration foundation; VPS Test preserved. The exact 7 original verification criteria are enumerated under `SCRUM-205` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/azure/main.bicep`, `deploy/azure/modules/network.bicep`, `deploy/azure/modules/nsg.bicep`, `deploy/azure/modules/container-apps-environment.bicep`, `deploy/azure/modules/managed-disk.bicep`.
- **Automated / real integration evidence:** Baseline CI Bicep and synthetic preflight/network/env tests pass. Infrastructure run 38153663507 succeeds with What-If/reconcile/private-network checks. Current effective cloud state is not independently inventoried.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Current resource/NSG/disk/ACA/private/public ingress/DNS/TLS/OIDC evidence and operational VPS Test proof.

#### Sprint 3 — [SCRUM-206: DO-303-Migrate ResearchTrack Production Services and Persistent Workloads to Azure](https://researchtrack.atlassian.net/browse/SCRUM-206)

- **Scope and acceptance:** Azure frontend/Gateway/backend workloads, runtime/probes/private addressing and persistence; retire old production dependency only after verification. The exact 8 original verification criteria are enumerated under `SCRUM-206` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/azure/vm/compose.yml`, `deploy/azure/scripts/render-containerapp.py`, `deploy/azure/scripts/deploy-container-apps.sh`, `deploy/mysql/reconcile-databases.sh`.
- **Automated / real integration evidence:** Fresh seven backend regressions/build and existing env composition/rollback tests; five disposable Kafka/upgrade tests. Historical a26cfd2 rollouts, 1810f095 unchanged image retention. This backend audit has no current frontend/VPS Test acceptance.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Current seven app image/revision/schema/routing/persistence inventory, frontend → Gateway behavior, data survival and Test/old-VPS independence.

#### Sprint 3 — [SCRUM-207: DO-304-Adapt CI/CD and Validate Azure Production Deployment](https://researchtrack.atlassian.net/browse/SCRUM-207)

- **Scope and acceptance:** Preserve VPS Test and build/tag/secrets; approved Azure releases, public/internal readiness, revision recovery and visible failures. The exact 8 original verification criteria are enumerated under `SCRUM-207` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `.github/workflows/backend-deploy-production.yml`, `.github/workflows/backend-deploy-azure-production.yml`, `.github/workflows/backend-release-validation.yml`, `deploy/azure/scripts/release-policy.py`, `deploy/azure/scripts/release-summary.py`.
- **Automated / real integration evidence:** Fresh 34 workflow/25 policy/12 reconciliation tests; unchanged-source TASK 11 synthetic failure matrix. Required CI/Kafka and restoration/smoke succeed in published app/infra runs. Public readiness skipped; main remains an active competing route.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Reviewed local fixes with exact-SHA CI, current trusted deployment-source coordination, public contract and authorized compatible revision recovery.

#### Sprint 4 — [SCRUM-201: DO-401 - Deploy and Configure Kafka Infrastructure](https://researchtrack.atlassian.net/browse/SCRUM-201)

- **Scope and acceptance:** Kafka 3.9.1 private KRaft/TLS/listeners, persistence, external configuration, topic approach and restart. The exact 6 original verification criteria are enumerated under `SCRUM-201` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `deploy/azure/vm/compose.yml`, `deploy/azure/vm/kafka/topics.json`, `deploy/azure/vm/scripts/kafka-identity.sh`, `deploy/azure/vm/scripts/verify-kafka.sh`, `deploy/azure/vm/scripts/diagnose-kafka-mounts.sh`.
- **Automated / real integration evidence:** 62 offline operation assertions + 8 Python methods fresh PASS; topics/config 21 fresh PASS; real disposable TLS Kafka startup/restart in TASK 09 and baseline CI. Previous mount cause is not confirmed by a live mount dump.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Read-only deployed mount/cluster identity/CA/advertised listener/retention/NSG, ACA-private and exporter evidence. Single-node/RF1 is not automatic failover.

#### Sprint 4 — [SCRUM-202: DO-402 - Configure Kafka Topics and Integrate ResearchTrack Event Flows](https://researchtrack.atlassian.net/browse/SCRUM-202)

- **Scope and acceptance:** Approved topics/contracts, GitHub/Jira producers/consumers, environment-specific connectivity, ownership and durable business processing. The exact 7 original verification criteria are enumerated under `SCRUM-202` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `src/BuildingBlocks/Kafka/KafkaPersistence.cs`, `src/BuildingBlocks/Kafka/KafkaMessagingWorkers.cs`, `src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubKafkaEventHandler.cs`, `src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs`, `deploy/azure/vm/kafka/topics.json`.
- **Automated / real integration evidence:** GitHub/Jira messaging 15/15 fresh; atomic rollback/dedup/identity tests in 289/106 fresh service regressions; TASK 09 real broker + MySQL 3/2 PASS, source hashes unchanged. Baseline CI 3/2 PASS covers older published code.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Authentic approved provider actions → retained webhook/outbox → Kafka receipt/offset → final business rows, deployed topic/CA/image evidence and restart without manual topic recreation.

#### Sprint 4 — [SCRUM-203: DO-403 - Implement Kafka Observability and Event Processing Alerts](https://researchtrack.atlassian.net/browse/SCRUM-203)

- **Scope and acceptance:** Broker/exporter/producer/consumer/lag/backlog/failure metrics, integrated Kafka panels, all ten alerts and privacy. The exact 6 original verification criteria are enumerated under `SCRUM-203` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `src/BuildingBlocks/Kafka/KafkaMetrics.cs`, `deploy/monitoring/rules/researchtrack-alerts.yml`, `deploy/monitoring/tests/kafka-alerts.test.yml`, `deploy/monitoring/grafana/provisioning/dashboards/json/kafka-observability.json`.
- **Automated / real integration evidence:** TASK 08/12 local 159 checks/33 scenarios/55 expressions, nine metrics tests and 21 operator queries; TASK 09 selected real scrape/fault transitions. Published baseline CI ran 159/30/55, not local 33.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Current deployed GitHub/Jira metric scrapes, exporter/targets/datasource/panels, bounded labels, and operational observation of approved events; no production outages.

#### Sprint 4 — [SCRUM-208: DO-404 - Validate Kafka Failure Recovery and Final Event-Driven Deployment](https://researchtrack.atlassian.net/browse/SCRUM-208)

- **Scope and acceptance:** Real event/business flows, controlled outage/reconnect/retry/monitoring, unaffected HTTP, repair-free ordinary restart and final smoke/runbook. The exact 8 original verification criteria are enumerated under `SCRUM-208` in the final checklist; full original scope/DoD is in the Jira evidence JSON.
- **Implementation:** `tests/Kafka/IsolatedKafkaFixture.cs`, `tests/Kafka/broker-control.sh`, `tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs`, `tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs`, `scripts/test-kafka-integration.sh`, `docs/devops/azure-production/KAFKA_OPERATIONS.md`.
- **Automated / real integration evidence:** TASK 09 fixture cfa1267c500d five PASS, zero skips: real outage, commit failure/rejoin, durable dedup/business, ordinary 128-record restart and selected readiness/metrics/alerts. All recorded source hashes match. Baseline CI five PASS precedes extended local assertions.
- **Deployment / operational evidence:** Baseline app/infrastructure execution is attributed above; current specific live story acceptance is **NOT VERIFIED**. Follow the linked TASK 02–13 report for safe operator verification.
- **Final status:** **IMPLEMENTED / TESTED LOCALLY within the described scope; final acceptance NOT VERIFIED / PENDING APPROVAL.** Missing: Final deployed authentic-provider/readiness/business/offset/monitoring acceptance; narrower transport exhaustion, distinct-event batches, Jira held batch and crash/disk cases remain NOT VERIFIED per TASK 09 matrix.


### Remaining operator steps and future main cutover

1. Supply authoritative Sprint 1 DevOps stories/criteria and complete the per-story
   checklist with links to acceptable current evidence. Do not infer acceptance from Done.
2. Review uncommitted TASK 02–13 fixes and this audit. Through a separately authorized
   integration task, incorporate current main features, resolve the seven overlapping
   committed files/two local overlaps and rerun the resulting source's tests.
3. Coordinate the conflicting main/integration production routes with the owner before
   another production release. Preserve main/default protections, develop VPS Test and
   required manual operations. No automatic switch or permanent integration protection
   is proposed. After approved publication, obtain exact-SHA CI/Kafka/build/deploy evidence.
4. During an existing approved runtime window, gather read-only Azure/OIDC/listener/disk/
   image/CA/topic/group/scrape evidence with the task reports. Do not wake/restart/reconfigure
   production merely for this audit or run Kafka `--restart`/`--smoke` automatically.
5. Verify public DNS/TLS/response contract; separately authorize enabling the existing
   readiness flag. Record public reachability and dependency health as different checks.
6. Separately authorize legitimate activity in a designated GitHub test repo and Jira
   test project. Capture redacted provider identity → durable records → Kafka receipt/
   offsets → final business outcomes → metrics/panels. Failure/recovery chaos remains
   confined to disposable environments; no production outage is required by this audit.
7. Verify Test/frontend operational acceptance, monitoring accumulation/failure/recovery
   and privacy, and representative performance/SLO evidence. Follow the existing task
   handoff instead of speculative caching/index/threshold changes.
8. Verify independent backup copies, protected recovery keys, freshness and representative
   coherent application/Kafka restore; approve RTO/RPO and revision recovery procedures.
9. For future main cutover, review a complete integration-to-main PR with preserved latest
   features and required checks. Plan trigger/policy source changes together, keep exact-SHA
   validation/rollback/concurrency/Environment/OIDC, and prevent dual production publishers.
   Perform a reviewed main release with live acceptance before retiring the temporary route.
   No PR, merge, settings change, dispatch or deployment is performed here.

TASK 14 changes only this audit and `FINAL_DEVOPS_ACCEPTANCE_CHECKLIST.md`; earlier
TASK 13 additionally changed the backup script, backup safety tests, disposable restore
runner and DR plan. Local ignored evidence remains under `artifacts/final-devops-task14/`
and `artifacts/disaster-recovery-task13/`. Current tests pass within their stated scope;
missing source/live evidence prevents complete DevOps acceptance.

## Historical audit snapshots — retained unchanged

Everything below this boundary predates TASK 14. Its SHAs/counts/branch comparison and
owner-setting recommendations describe the earlier snapshot and must not override the
current findings above. In particular, older proposals for permanent integration branch
protection and claims that integration contains latest main are superseded.
# Final DevOps Sprints 1–4 audit

## Current dated verification — 2026-10-11, a26cfd2 hardening review

HEAD was clean and exactly `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` before editing.
Fetched `origin/main` is `141181575e7b7fdda75e31356bfe9a980deadb7c`; integration is
16 commits ahead, zero behind. No newer source changes needed integration.

The application release gate is **IMPLEMENTED AND DEMONSTRATED**, superseding the
pre-publication statements in the original audit below. Read-only GitHub verification
confirmed [production run 38147705025](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38147705025)
and [PR Backend CI 38149500022](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38149500022)
succeeded at that exact SHA. Required CI, all five isolated tests, source eligibility,
seven image builds, six service migration log confirmations, all seven application
rollouts/health checks, infrastructure verification, exact runtime restoration and k6
smoke succeeded. The explicit public readiness step was **SKIPPED**. These executions
were performed before this task; no new workflow was triggered here.

Current read-only settings still show integration `protected:false`, no required checks,
and production `deployment_branch_policy:null`, `protection_rules:[]`. Active main/develop
rulesets do not establish protection of integration. Owner settings remain incomplete.

### Current issue classification and acceptance

| Item | Current classification / resolution | Evidence still needed |
|---|---|---|
| Application exact-SHA CI gate | Already fixed and demonstrated at a26cfd2; preserved | Owner branch/Environment protection; live rejection/recovery acceptance |
| Infrastructure CD CI gate | Confirmed missing validation; fixed locally using the existing reusable gate before Bicep and credentials | Reviewed publication and first gated infrastructure run |
| Production branch/Environment | GitHub configuration gap; exact UI/check-name checklist added | Owner-applied effective restrictions/reviewers |
| Public readiness | Acceptance gap plus confirmed redirect false-positive; optional check now requires HTTP 200 with TLS unchanged | Owner verifies configured URL/contract and enables existing variable |
| ARM reconciliation failure | Confirmed defect: failed deployment could report success on healthy existing revision; fixed with fatal result and preserved rollback | Published execution if separately approved |
| Smoke/restore reporting | Existing fatal job semantics retained; automated failure coverage and fail-closed metadata reporting expanded | Authorized recovery demonstration, no automatic cross-service/schema rollback |
| Jira latency | Performance investigation/handoff; no speculative app optimization | Representative per-stage timings/bytes/realistic EXPLAIN and agreed target |
| Kafka live chain | Acceptance gap; existing recovery/migration tests retained and rerun successfully | Approved provider events → business/offset/scrape/panel evidence |
| Actions/images/tool warnings | Maintenance recommendations, not proven deployment root causes | Action runner compatibility validation and deployed monitoring digest/version capture |

| Sprint acceptance | Current verified progress | Remaining final acceptance |
|---|---|---|
| Sprint 1 baseline | Preserved Auth/Project/Meeting/Submission/Gateway/Test functionality; fresh build and ordinary regressions pass | Original ticket mapping/criteria unavailable |
| Sprint 2 monitoring (SCRUM-194–197) | Config/rules/tests and production monitoring verification step succeed | Real target/panel/traffic/fire-resolve evidence |
| Sprint 3 Azure/CI (SCRUM-204–207) | Application trusted gate, builds, six migrations and seven rollouts demonstrated | Owner protections, current effective Azure/backup inventory, public readiness and reviewed rollback acceptance; infrastructure gate publication |
| Sprint 4 Kafka (SCRUM-201–203,208) | Required isolated suite five PASS/zero skips, historical upgrades/recovery/metrics/alerts; baseline gated CI demonstrated | Live mounts/identity/CA/settings and approved GitHub/Jira business/offset/metrics/dashboard chain; no production failure injection |

Fresh local verification: Release build zero warnings/errors; ordinary unit/mock coverage
**825 PASS, 5 conditional SKIPPED**, those five separately **PASS** in the disposable suite.
Business coverage **78.30% line / 64.50% branch**, unchanged floors 65/50. Release-policy/
summary **17**, infrastructure/public/restoration/What-If **16**, image-selection/failure/rollback
**16** regressions pass. Observability **159 config checks / 30 scenarios / 55 expressions**,
operational/env/composition/runtime/performance/Bicep checks also pass. Raw actionlint retains
ten baseline queue/style findings; qualified validation passes. Exact commands, all changed
files, limitations and artifact paths are in [FINAL_DEVOPS_HARDENING.md](FINAL_DEVOPS_HARDENING.md).

Latest k6 Jira issues p95 **3076.93 ms** (9/9 HTTP 2xx), GitHub activity **375.95 ms**;
overall p95 **1566.86 ms**, 61 business requests/0% failures. Existing overall threshold
3000 ms, endpoint map `{}`. Earlier Jira 3755.43 ms / eight requests below is historical,
not overwritten. Two tiny samples do not justify an endpoint SLO or threshold relaxation.
Isolated EXPLAIN uses existing unique project connection and project/IssueKey issue indexes,
no filesort, one synthetic row; it does not diagnose production latency.

**READY FOR USER CODE REVIEW; production acceptance remains incomplete.** This task made
local repository edits and read-only GitHub calls only; no commit, push, merge, PR creation,
workflow trigger, production resource/secret/settings/database/topic/offset change occurred.

## Original audit snapshot — before owner publication of a26cfd2

The original report below is retained as historical evidence, including its test failures,
old branch comparison and statements about the then-unpublished gate. Those statements
are superseded by the dated verification above; they are not current blockers for the
already-demonstrated application gate. Its owner protection/live acceptance gaps remain.

Audit date: **2026-10-11**. Repository: `Nimsara-Jayarathna/researchtrack-backend`.
Working tree: `integration/main-kafka-production-20261011`, local/uncommitted changes only.

**READY FOR USER CODE REVIEW. Production acceptance is not complete.** Required owner
protection settings, first published same-SHA release-gate evidence and live operational
acceptance remain pending. Nothing was committed, pushed, merged, deployed or dispatched
in this audit. Azure resources, GitHub secrets/settings, production brokers/databases,
DNS/TLS, topics, offsets and pending events were not changed. Frontend was not modified.

## A. Baseline and branch comparison

| Evidence | Value |
|---|---|
| Integration HEAD, unchanged by this task | `6c86bdb20d4682b56babc0afcd9a6bc53793d462` |
| Safely fetched `origin/main` | `141181575e7b7fdda75e31356bfe9a980deadb7c` |
| `git rev-list --left-right --count origin/main...HEAD` | `0 15`: zero behind, fifteen ahead |
| Initial working-tree status | Clean |
| Missing main commits / unresolved merge conflicts | None; main is an ancestor |
| Production source / repository default | Integration branch / main remains the repository default |

Baseline hashes/status/ancestry are retained in `artifacts/final-devops-audit/baseline.json`;
the initial classifications are in `INITIAL_AUDIT.md`. Requested code areas were inspected
before edits, including workflows/build selection, Bicep/VM/Compose, configuration,
GitHub/Jira services, shared Kafka, migrations, observability, performance and reports.

The fifteen extra commits are listed by `git log --oneline origin/main..HEAD`: Kafka
operations/TLS verification (`80eb1e3`, `c6f7c08`, `b664ef3`, `acd1445`), topic/config
integration (`8890208`), earlier branch policy (`ab2996c`), producers/consumers/outbox/receipt
(`e37b154`), fixture/CA delivery (`6911df7`), metrics and tests (`911cd5a`, `bdb8173`),
Azure integration/recovery (`464551d`, `9689734`), current branch policy (`79dd10f`),
and synthetic Compose/non-root promtool fixes (`b524840`, `6c86bdb`).

Auth, Project, Meeting, Submission, Gateway, existing GitHub/Jira sync/polling/authorization,
all historical migrations, approved contracts/topic definitions, dashboards/alert rules,
Bicep resources, Test/VPS behavior and infrastructure workflows are retained. No lasting application
production source changes remain; the controlled mutation harness temporarily changed
a Project boundary and restored it. Future main changes still require a fresh
comparison before release; this audit does not establish future branch equivalence.

## B. Actual story requirements and acceptance

Read-only Jira discovery used the actual epic `SCRUM-198` and direct DevOps/deployment
summary searches. Twelve stories were retrieved; their original scopes, verification and
DoD are retained without assignee/contact information in
`artifacts/final-devops-audit/jira-requirements.json`. Jira statuses are historical workflow
states, not a substitute for acceptance evidence. Jira was not updated.

**Sprint 1:** no original Sprint 1 DevOps ticket IDs/specifications were discoverable in
those searches or repository reports. Do not invent DO-101 or other tickets. Its technical
baseline (Docker service builds, GHCR, Test/VPS deployment, shared JWT/env composition,
service databases, migrations, health, original business features) was audited and tested
where locally possible. Ticket-by-ticket Sprint 1 acceptance remains **BLOCKED: owner must
supply the original story mapping/criteria**.

In the table, *pending* means **IMPLEMENTED — FINAL VERIFICATION PENDING**. No story is
newly marked COMPLETE solely from local tests or its Jira Done status.

| Sprint | Jira ID / story | Original scope → preserved implementation | Automated tests/evidence | Actual deployment evidence | Status / missing evidence |
|---|---|---|---|---|---|
| 2 | [SCRUM-194](https://researchtrack.atlassian.net/browse/SCRUM-194), DO-201 Prometheus | Private service scraping, availability/request metrics, monitoring independence → metrics endpoints/Prometheus configs | 159 config checks, promtool, real isolated scrape/transport outage | Historical Azure VM/monitoring verification step passed | Pending: actual Test targets, service stop/recovery and application behavior with Prometheus unavailable |
| 2 | [SCRUM-195](https://researchtrack.atlassian.net/browse/SCRUM-195), DO-202 Grafana | Persistent provisioning, system health/request/error/latency panels → existing overview/GitHub dashboards retained | Dashboard JSON/expression validation; original panel/rule preservation checks | Historical VM validation passed; no rendered UI captured | Pending: authenticated Grafana datasource/panel traffic/screenshots and visual outage/recovery |
| 2 | [SCRUM-196](https://researchtrack.atlassian.net/browse/SCRUM-196), DO-203 GitHub monitoring | Sync/webhook/reconciliation success/failure/duration and safe labels → existing service metrics/dashboard | Unit/mock metrics + actual isolated GitHub processing and failure metrics | Historical deployed app verification only | Pending: deployed sync/webhook/daily reconciliation metrics and controlled synthetic integration failure |
| 2 | [SCRUM-197](https://researchtrack.atlassian.net/browse/SCRUM-197), DO-204 basic alerts | Service/Gateway/error/sync/reconciliation alerting and recovery → original rules retained | 30 rule scenarios including existing service/integration rules | Historical monitoring verifier passed | Pending: Test service/integration alert fire/resolve evidence; external notification delivery is not required by original criteria |
| 3 | [SCRUM-204](https://researchtrack.atlassian.net/browse/SCRUM-204), DO-301 Jira monitoring | Jira sync/webhook/reconciliation/errors/panels → service metrics and Jira dashboard | Jira regression tests, config/rule/expression validation, actual isolated Jira flow | Historical deployed-app/monitoring checks passed | Pending: deployed Jira panels/reconciliation and failure/recovery demonstration |
| 3 | [SCRUM-205](https://researchtrack.atlassian.net/browse/SCRUM-205), DO-302 Azure foundation | Resource group/ACA/private networking/ingress/TLS/storage/secrets; preserve VPS Test → existing Bicep/VM layout | Two Bicep builds; preflight/network/IP/env/runtime simulations; three What-If cases | Existing infrastructure runs succeeded; no new cloud read/write | Pending: current Azure resource/NSG/disk/ACA/DNS/TLS/OIDC effective-state inventory and Test health evidence |
| 3 | [SCRUM-206](https://researchtrack.atlassian.net/browse/SCRUM-206), DO-303 migrate workloads | App migration plus VM persistent workloads/monitoring/config → seven ACA apps and persistent VM stack | Release build/regressions, env/secret composition, runtime/rollback stubs, real historical MySQL upgrades | Existing production application/infrastructure workflows succeeded | Pending: current image/revision/schema/data/backup and persistent workload verification; live migrations not run here |
| 3 | [SCRUM-207](https://researchtrack.atlassian.net/browse/SCRUM-207), DO-304 CI/CD/recovery | VPS Test unchanged, trusted Azure release/version/config/health/failure visibility → same-SHA gate added; existing OIDC/rollbacks preserved | 13 release-policy/summary tests, 12 branch/monitoring tests, 13 image/rollback cases, runtime tests | Historical CD success exposed independent CI risk; new gate not published | Pending: owner protections, first exact-SHA gated run, public health, authorized revision recovery and failed-CI deployment-block proof |
| 4 | [SCRUM-201](https://researchtrack.atlassian.net/browse/SCRUM-201), DO-401 Kafka infra | Private broker/listeners/security/persistence/startup/restart/config/topics → 3.9.1 KRaft/TLS/persistent cluster | 50 operational shell + 8 Python cases; TLS real fixture, persistence and mount rejection cases | Historical VM validation succeeded; owner previously reported deployment | Pending: live read-only mount/image/cluster/disk/NSG/TLS/connectivity diagnostics; no production restart authorized |
| 4 | [SCRUM-202](https://researchtrack.atlassian.net/browse/SCRUM-202), DO-402 topics/flows | Approved topics/contracts, event publish/consume, durable processing → approved v1 registry and same-service outbox/receipt workers | 21 topic/config tests; actual GitHub/Jira business flows; both historical DB upgrades | No current live synthetic-provider chain observed | Pending: real approved topic settings/public CA/secretRef/images, authentic provider events → business results/committed offsets/dedup |
| 4 | [SCRUM-203](https://researchtrack.atlassian.net/browse/SCRUM-203), DO-403 Kafka observability | Exporter/scraping/lag/producer-consumer errors/backlog/held/alerts → ten rules and Kafka dashboard | 159 config checks, 30 scenarios, 55 expressions; isolated fire/resolve metrics | Historical monitoring verifier success, not proof of current app Kafka metrics | Pending: deployed GitHub/Jira `/metrics`, actual targets/panels and alert evidence |
| 4 | [SCRUM-208](https://researchtrack.atlassian.net/browse/SCRUM-208), DO-404 failure recovery | End-to-end business effects, outage/restart/reconnect/metrics/unrelated HTTP/final smoke/runbook → preserved and expanded isolated suite | Five real fixture tests pass, zero skips; final health/lag smoke and durable handoff replay | No production outage/restart/deployment exercised | Pending: separately authorized final deployed synthetic flow/readiness/scrape/business/offset evidence; restart acceptance only in approved verification environment |

## C. CI/CD trust, provenance and release reporting

The confirmed critical defect was independent CI and production CD push triggers: a failed
CI could coexist with successful CD for the same SHA. Production now calls shared reusable
validation before image selection/build. Required checks comprise the unchanged static,
performance-validator, Bicep, env, Kafka operations/config/observability and .NET unit/mock/
coverage checks, **plus the isolated Kafka/recovery/historical-MySQL-upgrade job**.

Both push and manual production requests use the same validation. CI runs with
contents-read permission, without inherited production secrets, environment or OIDC. PR CI
remains available for develop/main/integration. Every release checkout is explicitly pinned
to `github.sha` with persisted Git credentials disabled. `release-policy.py` requires:

1. Required CI result exactly `success` (no failed/cancelled/skipped/missing/neutral fallback).
2. Exact repository and `refs/heads/integration/main-kafka-production-20261011`.
3. Only trusted push/manual events, not PR/PR-target/workflow_run/repository_dispatch.
4. A valid immutable SHA matching the current approved branch head through read-only API.
5. The same check again before production env materialization/OIDC or legacy SSH access.

GitHub loads a same-repository relative reusable workflow from the
[caller’s exact commit](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).
There is no privileged `workflow_run`, PR artifact execution or caller-selected deployment
SHA. These guards assume reviewed source on the approved branch; they do not replace owner
branch protection/code review. Freshness is checked twice, not continuously during rollout.
A later legitimate push queues behind the existing release; every rollout retains its exact SHA.

The generic legacy reusable path also requires successful production validation and
successful image build; Test calls still work when the production-only validator is skipped.
OIDC, production environment, contents/packages/id-token scoping, source fingerprints,
digest/OCI provenance checks, parallel matrix builds, Bicep What-If gate, six-service
migration-before-rollout checks, automatic per-app rollback and exact runtime restore are
preserved. Existing shared concurrency (`researchtrack-azure-production`, cancel false,
queue max) is unchanged. CI now occupies that lock before builds; the approximately seven
minute required recovery suite runs in parallel with ordinary CI. No duplicate independent
integration-branch CI push workflow was added.

The existing source-selection table distinguishes images built/reused. The application
summary distinguishes changed/unchanged, source provenance and revisions. A no-op explicitly
reports **no new version, migrations or rollouts**. New read-only run/attempt metadata reports
CI, workflow queue, parallel build wall time, infrastructure, public health and k6 status.
Per-service migration/rollout/revision-health seconds are recorded separately. Rerun queue
time is NOT AVAILABLE because the original run creation timestamp cannot measure it.
k6 session time includes runtime acquire/restore; existing k6 reports carry test-only timing.
Skipped public checks/smoke remain explicit. The summary cannot label a missing CI or failed
smoke a verified release. Actual new Actions execution is NOT EXECUTED until reviewed publication.

A failed smoke fails the release. Existing rollout failure rollback is automatic; there is
no newly invented automatic cross-service rollback after k6. Use the owner-reviewed previous
healthy revision plan, retaining compatible Kafka consumers and additive data/schema.

## D. Kafka, mounts, recovery and migrations

- Kafka 3.9.1, single-node KRaft/RF1. External private listener uses verified SSL/9092;
  INTERNAL/29092 is Docker-private, controller/9093 is localhost-only. Data/cluster ID,
  restricted runtime configuration and TLS keystore persist on the existing disk/binds.
  Broker restart policy/resources and NSG design are unchanged.
- Approved topics: `researchtrack.deployment-smoke`, `researchtrack.github.events.v1`,
  `researchtrack.jira.events.v1`; one partition/RF1, application retention 72 hours;
  no routine auto-create or destructive reconciliation.
- v1 webhook-reference contracts and GitHub/Jira groups are retained. TLS CA verification
  and HTTPS endpoint identification remain enabled. Only the existing public CA is delivered
  via secretRef; optional empty CA is omitted when disabled, and enabled production requires
  a valid public Base64 CA. Other required empty secrets fail. SSH/private keys are rejected.
- Atomic durable outbox/receipt, same-key ordering, idempotent acknowledgement and manual
  offsets after durable handoff remain. Bounded retries are 30s/2m/10m/30m/1h, maximum ten
  attempts; FAILED rows/held consumers retain data and require reviewed recovery.
- Metrics remain bounded service/stage/outcome/status labels. No provider tokens, private
  keys or event payload/identity labels are added. Ten alert rules, eighteen Kafka panels
  and pre-existing monitoring are retained. External notification delivery is optional.

The earlier mount assertion likely concerned two image-declared anonymous volumes, but
**the production cause is unconfirmed without a live mount dump**. The current verifier
already requires all five exact binds, read-only identity/config mounts, no duplicate or
nested/shadow destinations, and at most the two declared extra destinations
`/etc/kafka/secrets` and `/mnt/shared/config`. Extras must be writable Docker volumes with
64-hex anonymous names and matching Docker volume data paths. Named volumes, extra host
binds, missing binds, wrong data roots and writable protected config are rejected. All
related regressions pass; the narrow correction was already present. No new weakening or
mount change was needed. Use `diagnose-kafka-mounts.sh` and `verify-kafka.sh --check` for
read-only attribution; **do not use `--restart` in production**. If the live dump exactly
matches these allowed mounts but the deployed verifier still uses the obsolete five-mount
assertion, the narrow proposed correction is delivery of only the reviewed verifier script
through the owner's approved context, followed by `--check`. Do not recreate the broker,
change Compose mounts or remount the data disk to satisfy that assertion.

The final isolated suite preserves actual provider ingress/authorization, production
publish/consume workers and business processing, with synthetic provider clients:

| Test | What it proves |
|---|---|
| GitHub recovery/business flow | Signed ingress → durable MySQL/outbox → TLS acknowledgement → handoff/receipt → existing worker/snapshot/commit; outages before publish and after handoff; retry/rejoin/offset continuity; replay makes one logical business result |
| GitHub native producer timeout | Real unavailable-loopback native client timeout; accepted webhook/outbox remain durable for retry (SQLite in this failure-only test) |
| Jira recovery/business flow | Authenticated issue update → MySQL/outbox → TLS publish → scheduling lock/receipt/job → existing sync worker → issue snapshot/revision; same outage/replay/commit protection |
| GitHub historical upgrade | Real twelve-migration chain; pre-Kafka repository/source/link rows; twelve old tables/three records preserved; new identity/retry indexes and outbox/receipt round trip; reapply no-op |
| Jira historical upgrade | Real six-migration chain; pre-Kafka connection/issue rows; eight old tables/two records preserved; same new schema/index/persistence and reapply assertions |

Upgrade databases never use EnsureCreated. Flow databases still use it, without duplicating
historical upgrade tests. Existing schema hashes include keys/indexes/foreign-key definitions;
record hashes are compared without publishing records. All earlier migrations remain unchanged.
The new migration adds only two Kafka tables/three outbox indexes; SQL/model unit assertions
and actual MySQL upgrade pass. Old database tables are structurally compatible, but rolling
back to a pre-Kafka image can strand accepted events; preserve a Kafka-capable rollback revision.

First full run: GitHub 3 PASS; Jira 1 PASS/1 FAIL (`Local_UnknownPartition` in fresh-client
watermark lookup). This exposed a fixture metadata warm-up race, not a lost topic. A test-only
bounded retry now includes that transient local error within the existing 60-second deadline.
Two added unit regressions verify successful metadata retry and immediate authorization-error/
cancellation rejection. Production retry logic/offsets were untouched. Final full rerun:
**five PASS, zero FAIL/SKIP**; no manual topic recreation, seek/reset, table repair or outbox clear.

Actual final group state: GitHub next offset/log end **130/130, lag 0**; Jira **2/2, lag 0**.
Broker identity/data survived restart; failure/recovery counters and broker/exporter alert
transitions were observed. GitHub held/lag/processing-failure scenarios remain. HTTP live,
Swagger and readiness stayed available where dependencies permitted; messaging readiness was
Healthy → Degraded → Healthy. Degraded uses the existing HTTP-200 policy; held is Unhealthy.
A broker outage with no attempted publication need not immediately degrade app readiness.

## E. Executed tests, commands and evidence

Environment: macOS arm64, .NET SDK 10.0.400, Docker Desktop 29.2.1/Compose 5.1.0,
Homebrew Bash 5, Python 3, OpenSSL, actionlint 1.7.12, ShellCheck. Tests dated 2026-10-11.
Native Bicep 0.48.1 was downloaded from the official release and its published SHA256 checked;
no Azure CLI/login/deployment was used. Final .NET build/coverage ran after controlled source
restoration; use the final evidence rather than earlier intermediate coverage snapshots.

| Command/check | Honest final result | Artifact relative to repository |
|---|---|---|
| `dotnet restore ResearchTrack.sln --verbosity minimal` | PASS | `artifacts/final-devops-audit/restore.log` |
| `dotnet build ResearchTrack.sln -c Release --no-restore --verbosity minimal` | PASS, zero warnings/errors | `artifacts/final-devops-audit/build-final.log` |
| `COVERAGE_MIN_BUSINESS_LINE=65 COVERAGE_MIN_BUSINESS_BRANCH=50 /opt/homebrew/bin/bash scripts/coverage.sh --no-build` | PASS: **825 passed, 5 conditional Kafka/upgrades SKIPPED**, zero failures; those five separately pass below | `artifacts/final-devops-audit/coverage-verified.log`, `artifacts/coverage/` |
| Business coverage gates | PASS: **78.30% line / 64.50% branch** (floors 65/50 unchanged); filtered all-source 66.69/51.52 | `artifacts/coverage/coverage-metrics.env` |
| `dotnet test tests/ResearchTrack.{GitHub,Jira}Service.Tests -c Release --no-build --filter 'FullyQualifiedName~Kafka&Category!=KafkaIntegration'` | PASS: GitHub **44**, Jira **11**, zero skips (included in ordinary totals) | `artifacts/final-devops-audit/kafka-unit/` |
| `/opt/homebrew/bin/bash scripts/test-kafka-integration.sh` | PASS: GitHub **3**, Jira **2**, zero skips; first run FAIL retained | `artifacts/final-devops-audit/kafka-integration{,-final}.log`; final `artifacts/kafka-integration/rt-kafka-fixture-e391939d088a/` |
| `python3 deploy/azure/validation/test_kafka_topics_config.py` | PASS **21** tests (TLS/public CA/disabled empty/secretRef/app+jobs included) | `artifacts/final-devops-audit/test_kafka_topics_config.py.log` |
| `bash deploy/azure/validation/test-kafka-operations.sh` | PASS **50 shell + 8 Python** cases | `artifacts/final-devops-audit/test-kafka-operations.sh.log` |
| `bash deploy/azure/validation/test-kafka-smoke.sh` / `test-network-probe.sh` | PASS **9 / 12** cases | Corresponding `artifacts/final-devops-audit/*.log` |
| `bash scripts/test-kafka-observability.sh` | PASS **159 config checks / 30 promtool rule scenarios / 55 parsed dashboard expressions** | `artifacts/final-devops-audit/test-kafka-observability.sh.log` |
| `bash deploy/azure/validation/test-validate-azure-env-files.sh` / `test-service-env-composition.sh` | PASS **22 / 10** cases, synthetic configuration only | Corresponding audit logs |
| `python3 deploy/azure/validation/test_release_policy.py` | PASS **13** trust/graph/summary tests | `artifacts/final-devops-audit/release-final-0.log` |
| `python3 deploy/azure/validation/test_monitoring_reconciliation.py` | PASS **12** tests | `artifacts/final-devops-audit/release-final-1.log` |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS **13** image/provenance/no-op/plan/migration-abort/rollback cases | `artifacts/final-devops-audit/release-final-2.log` |
| `bash deploy/azure/validation/test-{preflight,runtime-power,runtime-readiness,private-ip-check}.sh` | PASS **5 / 11 / 4 / 10** cases | Corresponding audit logs |
| `bash deploy/validate-shared-auth-wiring.sh` | PASS | `artifacts/final-devops-audit/validate-shared-auth-wiring.sh.log` |
| Native `bicep build deploy/azure/main.bicep --stdout` and `modules/container-app.bicep` | PASS both, verified official binary; no compiler warnings | `artifacts/final-devops-audit/bicep-results.json`, `bicep-{main,container-app}.log` |
| Existing `whatif-gate.sh` with disposable `az` stub | PASS **3** cases: safe changes, deletion refusal, existing explicit review override | `artifacts/final-devops-audit/whatif-results.json`, `whatif-*.log` |
| Bash `-n` on tracked deployment/scripts/Kafka/performance shells | PASS **87** files | `artifacts/final-devops-audit/shell-syntax-results.json` |
| Python compilation of renderer/config/impact/new release helpers | PASS | Executed in local validation; no production input |
| Raw `actionlint -no-color` | **FAIL**, existing 5 queue compatibility + 5 SC2129 style findings | `artifacts/final-devops-audit/actionlint-raw.log` |
| `actionlint -no-color -ignore 'unexpected key "queue" for "concurrency" section' -ignore 'SC2129:'` | Qualified PASS; only these existing categories excluded, no new findings | `artifacts/final-devops-audit/release-final-3.log` |
| ShellCheck changed/test fixture shells | Warning-severity PASS; raw informational SC1091 unresolved source path | `artifacts/final-devops-audit/shellcheck-{warning,raw}.log` |
| `bash tests/performance/{validate,test-report,test-run-vps,test-discover-vps-ip,test-gateway-exemption}.sh` | PASS all five scripts; initial macOS VPS mock FAIL fixed with guarded Linux-command stubs | Audit logs, `static-final-results.json` |
| Seven `dotnet test tests/*MutationTests/*MutationTests.csproj -c Release --no-restore` harnesses | PASS **541** tests, zero skips | `artifacts/final-devops-audit/mutation-harness-results.json`, per-project logs |
| `bash scripts/mutation-harness-verify.sh` | PASS: original code passes, deliberate boundary mutant causes **3 expected failures**, restored code passes | `artifacts/final-devops-audit/mutation-controlled.log` |
| Read-only `service-impact.py impact --head HEAD --base origin/main` | PASS; actual history/classification inspected, no commits | `artifacts/final-devops-audit/image-impact-readonly.log` |
| `python3 deploy/build/test_service_impact.py` full synthetic history harness | **NOT EXECUTED**: it creates fixture Git commits; strict task prohibits committing. No substitute claimed equivalent | Existing unchanged harness; operator must authorize fixture-only commits for that check |
| Non-production live k6 smoke | **NOT EXECUTED**: no local k6 executable or configured authorized full-stack nonproduction dataset/accounts | Validators/mock orchestration passed; real performance cannot be inferred |
| Other service `Category=DatabaseIntegration` suites | **NOT EXECUTED**: their dedicated databases/credentials were not provisioned by the Kafka-only fixture | Explicit coverage filter retained; Kafka upgrade/live-flow results do not cover these suites |
| Actual new GitHub gate/pipeline run or Azure live checks | **NOT EXECUTED**, prohibited publication/deployment/dispatch | Owner actions below |

Counts above are not summed across overlapping suites. Focused Kafka tests are included
within ordinary unit totals; migration tests are included within the five isolated cases.
The controlled-mutant failures are expected proof, not failures on restored source.

Historical read-only evidence: [Backend CI attempt 2](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38135611473)
passed for baseline SHA; [production CD](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38135611889)
passed and public HTTP verification was skipped, with k6 executed. These runs predate this
local gate and do not prove it. The previously authorized
[all-service Stryker run](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38141682006)
completed SUCCESS for the unchanged baseline SHA at the final read. This was a previously
authorized run, not a new dispatch; the downloaded summary artifact confirms all seven gates PASS,
**69.89% overall selected-business mutation score** against the unchanged 65% floor,
1,954 killed / 842 surviving mutants, zero timeouts/no-coverage mutants. This is baseline
application evidence, not execution of the unpublished DevOps changes. The sanitized service
scores are in `mutation-remote-summary.json`; no new run was triggered. Sanitized run/job/warning/performance metadata is in the audit artifact directory.

### Reproduce safely on macOS

```bash
cd /Users/dev/Desktop/z/researchtrack-backend-integration-main-kafka
git branch --show-current
git status --short
docker info
docker compose version
dotnet --version
export PATH="$(brew --prefix openssl@3)/bin:$PATH"
lsof -nP -iTCP:19092 -iTCP:13307 -iTCP:19090 -sTCP:LISTEN
# No listeners; do not stop unrelated containers or load real env files.
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
COVERAGE_MIN_BUSINESS_LINE=65 COVERAGE_MIN_BUSINESS_BRANCH=50 \
  /opt/homebrew/bin/bash scripts/coverage.sh --no-build
/opt/homebrew/bin/bash scripts/test-kafka-integration.sh
/opt/homebrew/bin/bash scripts/test-kafka-observability.sh
python3 deploy/azure/validation/test_release_policy.py
/opt/homebrew/bin/bash scripts/mutation-harness-verify.sh
# Run mutation verification serially, then rebuild before further coverage.
dotnet build ResearchTrack.sln -c Release --no-restore
git diff --check
```

Use the command table for the remaining offline validations. Missing Docker returns
NOT EXECUTED/exit 2; assertions fail with exit 1. Success requires five executed fixture
cases, zero skips, final health/scrape/lag smoke, exit 0. The runner refuses remote Docker
and non-loopback Kafka/MySQL endpoints. It stops only its random test project and retains
containers/volumes/messages/database/temp data. Never invoke the control script against
another project. Retained private fixture TLS/config/init files contain disposable secrets;
keep them outside published evidence. Do not remove volumes/data as part of this task.

## F. Findings and remaining risks

| Severity / type | Finding and resolution/next evidence |
|---|---|
| CRITICAL, confirmed release bug | Independent same-SHA CI/CD; corrected locally with shared unprivileged validation and trusted exact-source gating. New GitHub execution pending publication |
| HIGH, confirmed security configuration gap | Read-only production Environment: `deployment_branch_policy:null`, `protection_rules:[]`; source branch also reports `protected:false`, no required checks. Owner protection is mandatory before final acceptance |
| HIGH, deployment risk | Old main/feature workflow copies can still deploy until environment restrictions block them. Changing this branch alone cannot disable those refs; builds may still publish mutable tags before an Environment gate |
| HIGH, acceptance gap | Actual Azure CA/metrics/app event flow/migrations/mount/identity/disk/NSG/backup/revision recovery not observed in this task. Historical green CD is not complete acceptance |
| MEDIUM, test coverage gap, fixed locally | Default ordinary unit runs skip conditional Kafka tests; reusable required isolated job now executes all five. Manual trigger preserved |
| MEDIUM, migration gap, fixed locally | EnsureCreated-only flow fixture; real historical MySQL upgrades now tested with record/schema/index/no-op assertions |
| MEDIUM, confirmed fixture defect, fixed locally | Fresh client `Local_UnknownPartition` metadata race; bounded fixture retry and authorization/cancellation regressions, full recovery rerun passed |
| MEDIUM, performance concern | Real historical Jira p95 **3755.43 ms**, 8/8 successful requests; sample too small for an SLO diagnosis. No endpoint threshold configured; aggregate smoke pass does not prove Jira latency acceptable |
| MEDIUM, existing deployment risk | Production Prometheus/Grafana use `latest` tags. Pinning needs current deployed version/digest and persistence compatibility review; no speculative downgrade/reconcile performed |
| LOW, tooling warnings | Actions checkout/upload/login and Docker login emitted Node 20 deprecation/forced Node 24 warnings; historical jobs succeeded. Plan supported action-version upgrades in a separately verified change, not arbitrary major bumps |
| LOW, tooling compatibility | actionlint 1.7.12 rejects existing `queue:max`; five existing SC2129 style suggestions. Preserve working queue/security behavior; qualified lint passes |
| INFORMATIONAL, tooling | Azure containerapp extension reports command behavior changed by the extension; historical command success, no observed failure attributable to it. CLI/extension pinning requires separate compatibility evidence |
| INFORMATIONAL, historical issues fixed | Missing production grafana.env and 0700 temp directory/non-root promtool failures were corrected at baseline commits. Current Docker observability validation passes; no secret/perms weakening introduced |
| INFORMATIONAL, optional verification | Current historical public endpoint check was SKIPPED. k6 did execute. Keep skips explicit and review the public-check flag for final acceptance |
| INFORMATIONAL, documentation | Older reports have dated current-status notes and retain historical actions/failures/counts |

Single-broker RF1 protects normal restart, not disk/VM loss or high availability. Server TLS
uses private networking rather than client mTLS/SASL/ACL; this existing security model is
unchanged. Retry-budget exhaustion is retained/manual recovery, not guaranteed automatic
recovery. Smoke failure requires reviewed cross-service rollback, and optional smoke/public
flags must be reviewed. Sprint 1 ticket mapping and full synthetic Git-history test permission
remain missing. Final Azure acceptance and supported monitoring/action version updates are
not claimed complete.

### Jira performance handoff (no app contract change)

The existing production artifact `11702145451` from run `38135611889` confirms Jira issues
p95 3755.43 ms, average 2348.83, range 1419.26–3786.24, eight HTTP-2xx requests, zero errors.
The run used existing `K6_SMOKE_P95_MS=3000`, endpoint threshold map `{}`; this task did not
change it or any thresholds. Sanitized endpoint/threshold facts are retained in
`historical-jira-performance.json`.

`JiraIssueQueryService.GetIssuesAsync` first awaits Project Service authorization, queries
one Jira connection, then performs one project-filtered, IssueKey-ordered, no-tracking issues
query including DescriptionJson. No Atlassian HTTP request or N+1 per-issue query exists in
this path. There is no pagination; it returns the full stored project dataset. Existing
indexes cover project, `(project,IssueKey)`, `(project,StatusCategory)` and identity. There is
no demonstrated missing index. No server-side cache was observed in this read path.

Jira/application owner should measure authorization-hop, DB and serialization timings,
response bytes/issue count/DescriptionJson sizes, cold versus warm requests and realistic
concurrency on an authorized test dataset; capture the read-only generated query and
`EXPLAIN` plan using the actual project parameter. Verify index selection rather than adding
one blindly. Review pagination/projection/cache only with API/auth/freshness compatibility
and regression tests. Decide an endpoint SLO/threshold through the existing endpoint map
after review, not by relaxing aggregate or endpoint limits. No speculative app rewrite occurred.

## G. Owner/operator review and proposed acceptance

These are **proposed actions**, not operations performed here. Cloud writes, workflow
publication/deployment and settings changes require the owner's separate decision.

1. Review local diff/changed files and final artifacts. Confirm the baseline SHA/source
   branch. Supply Sprint 1 ticket mapping and authorize fixture-only Git commits if the
   synthetic image-impact harness should be run. Resolve nonproduction k6 dataset/accounts
   before running real performance; do not use production credentials in local fixtures.
2. In **GitHub Settings → Environments → production → Deployment branches and tags**, choose
   **Selected branches and tags**, retain only exact **Branch**
   `integration/main-kafka-production-20261011` (not a tag/wildcard/main/feature branch).
   Preserve secrets/OIDC. Add the approved required reviewer(s), prevent self-review where
   available, and review bypass permissions/queued runs. No such settings were changed here.
3. In **Settings → Rules → Rulesets / Branch protection**, review an exact-source-branch
   rule requiring PR/code-owner approval and current successful Backend CI plus isolated
   Kafka checks, no force-push/deletion, and restricted bypass/direct writes. Existing
   CODEOWNERS names the owner. Use the actual PR check names after they are registered;
   do not guess check contexts. Environment approval should remain in place while publishing
   the first gated release. Review old main/feature workflow copies and concurrent runs.
4. Review variables `K6_AUTO_SMOKE_ENABLED=true` and
   `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true` for final release acceptance. Preserve existing
   approved thresholds, credentials, OIDC claims and What-If override policy. The first
   published workflow should prove CI failure/cancellation/missing/stale/PR/wrong-ref cannot
   reach image/deployment jobs. This audit ran offline policy cases, not live gate injections.
5. Before any authorized deployment, inspect current infrastructure, broker/data/CA/topic
   state, image/revision/migration history and backup/restore readiness. Use the existing
   authorized operations context, not a new SSH credential or replacement broker/CA.
6. If/when release is separately authorized, use the existing protected pipeline for the
   reviewed SHA, observe required CI before builds, digest/provenance and per-service no-op
   rows, same-image dbcheck/migrate jobs, healthy revisions, VM/network verification,
   runtime restoration and k6. Do not accept a no-op as a newly deployed version.
7. On separately authorized synthetic provider resources, collect authentic GitHub/Jira
   ingress → persisted outbox → acknowledgement → receipt/job → business snapshot/revision
   → committed offsets and duplicate protection, plus real deployed `/metrics`/Prometheus/
   Grafana evidence. Ensure timestamps establish Kafka handoff causality rather than
   existing polling having completed first. Update Jira only when actual criteria hold.

Read-only review commands (do not dispatch workflows):

```bash
gh run list --repo Nimsara-Jayarathna/researchtrack-backend \
  --branch integration/main-kafka-production-20261011 --limit 20
gh api repos/Nimsara-Jayarathna/researchtrack-backend/environments/production \
  --jq '{deployment_branch_policy,protection_rules}'
gh api repos/Nimsara-Jayarathna/researchtrack-backend/branches/integration%2Fmain-kafka-production-20261011 \
  --jq '{name,protected,protection}'
# Existing authorized Azure CLI context only; these are reads, no login/settings change.
az containerapp show -g rg-researchtrack-prod -n rt-jira-prod \
  --query '{ready:properties.latestReadyRevisionName,image:properties.template.containers[0].image}'
az containerapp revision list -g rg-researchtrack-prod -n rt-jira-prod -o table
curl -fsS --max-time 10 https://api.researchtrack.blipzo.xyz/health/ready
```

VM-side read-only diagnostic text, through the owner's existing authorized context.
Azure VM Run Command submission itself requires separate operational approval:

```bash
/opt/researchtrack/scripts/diagnose-kafka-mounts.sh
/opt/researchtrack/scripts/verify-kafka.sh --check
/opt/researchtrack/scripts/kafka-topics.sh reconcile --check
/opt/researchtrack/scripts/kafka-topics.sh describe researchtrack.github.events.v1
/opt/researchtrack/scripts/kafka-topics.sh describe researchtrack.jira.events.v1
openssl x509 -in /data/researchtrack/kafka/tls/ca.crt -noout -dates -fingerprint -sha256
```

Database owner read-only statements on each existing service database, using approved
credentials without recording them:

```sql
SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;
SHOW CREATE TABLE kafka_outbox_messages;
SHOW CREATE TABLE kafka_inbox_receipts;
SELECT Status, COUNT(*) FROM kafka_outbox_messages GROUP BY Status;
SELECT COUNT(*) FROM kafka_inbox_receipts;
```

Prometheus/Grafana evidence must include broker health, exporter UP, pending/FAILED outbox,
producer/consumer/commit failures, held status, consumer lag and firing/resolved alerts.
Record UTC, approved synthetic correlation, image digest/revision and expected business
result. Do not publish env files, private keys, provider bodies/tokens or real user records.

### Safe rollback plan (proposal only)

Use the recorded **previous healthy Kafka-capable revisions/digests**, not guessed mutable
tags. Capture current revisions/runtime state/topic identity/offsets/pending counts first;
verify the target's schema/contracts and work compatibility. Automatic rollout failures
already deactivate failed revision and keep/reactivate the prior healthy revision.
After k6 failure or another accepted rollback decision, the owner may use the existing
revision path, for example (never executed here):

```bash
PREVIOUS_JIRA_REVISION='exact-previous-healthy-revision-from-reviewed-release-summary'
az containerapp revision activate -g rg-researchtrack-prod -n rt-jira-prod \
  --revision "$PREVIOUS_JIRA_REVISION"
# Verify active routing/image/readiness, metrics and pending work; repeat only for affected apps.
```

Coordinate this through the shared protected runtime/concurrency context. Restore exactly
the recorded original power/routing state using the existing recovery artifact/runbook;
do not restart an off production stack ad hoc. Retain additive Kafka tables, all historical
data, disk/cluster ID, certificates, topics, offsets and pending/FAILED rows. Never run
migration Down, clear events, reset offsets or recreate volumes to roll back. A live mount
change requires its own diagnosed, backed-up, separately approved correction plan.

## H. Definition of Done and readiness

- [x] Initial clean branch/audit/hash comparison; all fetched main changes present.
- [x] Twelve actual Jira DevOps specifications inspected; no invented tickets/status updates.
- [x] Safe local exact-SHA reusable CI gate, pre-production recheck and policy/summary regressions.
- [x] Existing manual workflows, OIDC/concurrency/infra safety, image provenance/no-op/rollback preserved.
- [x] Kafka topics/TLS/persistence/outbox/receipt/manual commit/retries/metrics/ten alerts preserved.
- [x] Five disposable flow/recovery/historical-upgrade tests executed successfully, zero skips.
- [x] Real business/dedup/offset/readiness/alert/health smoke evidence retained; first failure retained.
- [x] Release/.NET coverage/ordinary and focused Kafka units/541 harness tests/controlled mutant executed.
- [x] Static/config/operational/observability/Bicep/qualified workflow/shell checks executed honestly.
- [x] Historical reports labeled and current runbook/test/summary/operator/rollback guidance updated.
- [x] No production/cloud/secret/frontend/commit/push mutation; local review only.
- [ ] Owner supplies Sprint 1 original acceptance mapping.
- [ ] Full synthetic Git-history harness authorized/executed; nonproduction live k6 and remaining DB suites.
- [ ] Owner restricts production Environment and reviews source branch/reviewer/check protections.
- [ ] Reviewed publication demonstrates new Actions gate for the exact SHA and release summary.
- [ ] Current Azure mount/network/disk/TLS/CA/image/migration/backup inventory accepted.
- [ ] Real deployed provider flow/business/offset/dedup/metrics/panels and approved recovery evidence.
- [ ] Jira latency diagnosis/SLO reviewed; no claim a smoke aggregate proves endpoint latency.

Local code is reviewable and its executed checks pass with disclosed raw lint/tool warnings
and unexecuted checks. **Final production story acceptance is pending owner actions and live
proof; this report is not authorization to publish/deploy.**

## I. Exact changed-file list

- `.github/workflows/backend-build-images.yml`
- `.github/workflows/backend-ci.yml`
- `.github/workflows/backend-deploy-azure-production.yml`
- `.github/workflows/backend-deploy-production.yml`
- `.github/workflows/backend-deploy-reusable.yml`
- `.github/workflows/backend-release-validation.yml`
- `.github/workflows/kafka-integration-tests.yml`
- `deploy/azure/scripts/deploy-container-apps.sh`
- `deploy/azure/scripts/release-policy.py`
- `deploy/azure/scripts/release-summary.py`
- `deploy/azure/validation/test-deploy-image-selection.sh`
- `deploy/azure/validation/test_monitoring_reconciliation.py`
- `deploy/azure/validation/test_release_policy.py`
- `docs/devops/FINAL_DEVOPS_SPRINTS_1_TO_4_AUDIT.md`
- `docs/devops/azure-production/04_PRODUCTION_CICD.md`
- `docs/devops/azure-production/DEVOPS_4_4_REPORT.md`
- `docs/devops/azure-production/IMPLEMENTATION_STATUS.md`
- `docs/devops/azure-production/KAFKA_INTEGRATION_TESTING.md`
- `docs/devops/azure-production/KAFKA_OBSERVABILITY.md`
- `docs/devops/azure-production/KAFKA_OBSERVABILITY_TASK_REPORT.md`
- `docs/devops/azure-production/KAFKA_OPERATIONS.md`
- `docs/devops/azure-production/KAFKA_TASK_REPORT.md`
- `docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md`
- `docs/devops/azure-production/MAIN_KAFKA_INTEGRATION.md`
- `docs/devops/azure-production/README.md`
- `docs/devops/azure-production/RUNBOOK.md`
- `scripts/coverage.sh`
- `scripts/test-kafka-integration.sh`
- `tests/Kafka/IsolatedKafkaFixture.cs`
- `tests/Kafka/KafkaTestDatabase.cs`
- `tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs`
- `tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs`
- `tests/performance/test-run-vps.sh`


## TASK 14 current final decision

The historical snapshots above do not close the current Sprint 1/source, latest-main,
competing deployment route, public readiness, real-provider/monitoring and recovery
evidence gaps. Follow the current dated audit and acceptance checklist. No publication
or production operation is authorized or performed by this audit.

**BLOCKED — ACCEPTANCE EVIDENCE MISSING**
