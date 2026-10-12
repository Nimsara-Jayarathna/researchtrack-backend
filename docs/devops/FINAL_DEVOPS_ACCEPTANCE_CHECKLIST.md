# Final DevOps acceptance checklist

TASK 14 — 2026-10-12. Source and evidence boundaries are in
[the current audit](FINAL_DEVOPS_SPRINTS_1_TO_4_AUDIT.md).

**BLOCKED — ACCEPTANCE EVIDENCE MISSING.** Boxes below mean final criterion acceptance,
including the stated deployed environment and owner review. Local/CI proof is recorded
even where a box remains open. No Jira state or deployment setting is changed.

## Evidence and source prerequisites

- [x] Inspected integration HEAD `1810f09535d6a6d00947bcf84d18fde473e40c39` and local diff.
- [x] Read-only fetched main `c79789d977d733aeb3ed2d247e0b4c746f300cbc`: integration 17 ahead / 14 behind.
- [x] Verified main is permanent default and existing protection remains; no integration ruleset proposed.
- [x] Verified latest integration app/infra required CI/Kafka runs and their exact SHAs/counts.
- [x] Executed fresh Release build, 849 ordinary passes / 5 conditional skips, 98 offline Python passes.
- [x] Checked applicability of retained local recovery/failure/tooling evidence by recorded file hashes.
- [ ] Obtain authoritative Sprint 1 DevOps story IDs, scope and criteria; Confluence read returned 403.
- [ ] Review/integrate newer main runtime security/features and revalidate resulting source through separately approved work.
- [ ] Review and publish local fixes through the owner process; obtain CI for that immutable published SHA.
- [ ] Coordinate conflicting automatic main/integration production routes without permanent integration protection.
- [ ] Retain an approved evidence record: UTC time, source SHA/diff hash, run/job IDs, target, result, redactions and operator sign-off.

## Sprint 1 — source unavailable

No ticket IDs invented. Docker/GHCR/VPS Test/shared JWT/config/migrations and seven-service
baseline are IMPLEMENTED / TESTED LOCALLY. Ticket-level acceptance is NOT VERIFIED until
the owner supplies the authoritative specification or missing read-only source access.

## Sprint 2 — SCRUM-194: DO-201-Integrate Prometheus Metrics Collection

Authoritative source: [SCRUM-194](https://researchtrack.atlassian.net/browse/SCRUM-194). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Configuration validator and promtool; TASK 09 real fixture scrape/selected transport outage. CI job 114512094615 validates the published configuration. Full Sprint 2 Test target accumulation and application use with Prometheus stopped are not verified.

**Final acceptance / missing proof:** Actual configured Test targets UP, accumulating metrics and approved Test service/Prometheus outage-recovery demonstration.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Prometheus starts successfully after deployment. **DEPLOYED baseline monitoring validation; current process/target evidence NOT VERIFIED**.
- [ ] Expected ResearchTrack services appear as configured targets. **IMPLEMENTED / TESTED LOCALLY / VERIFIED IN CI for target configuration; current targets NOT VERIFIED**.
- [ ] Healthy services report an UP/healthy target state. **TESTED LOCALLY for fixture targets; complete deployed service target inventory NOT VERIFIED**.
- [ ] Stopping one test service causes its target to become unavailable. **NOT VERIFIED for Sprint 2 backend-service stop; deterministic service rules tested locally**.
- [ ] Restarting the service restores the target to healthy state. **NOT VERIFIED for that Test service restart; deterministic recovery rules tested locally**.
- [ ] Metrics continue to accumulate over time. **TESTED LOCALLY for fixture scrape changes; sustained Test accumulation NOT VERIFIED**.
- [ ] ResearchTrack remains usable if Prometheus is stopped. **NOT VERIFIED: no full application-use test with Prometheus stopped**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 2 — SCRUM-195: DO-202-Deploy Grafana and Build ResearchTrack Service Dashboard

Authoritative source: [SCRUM-195](https://researchtrack.atlassian.net/browse/SCRUM-195). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Dashboard JSON/query parsing and provisioning tests; CI monitoring verifier succeeds. No authenticated rendered Grafana UI/datasource/traffic demonstration was collected.

**Final acceptance / missing proof:** Rendered overview, datasource health, populated request/error/latency panels, persisted configuration and visual Test outage/recovery.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Grafana starts and can query Prometheus successfully. **DEPLOYED baseline stack/provisioning; authenticated datasource query NOT VERIFIED**.
- [ ] The ResearchTrack dashboard loads without manual ad-hoc configuration after deployment. **IMPLEMENTED / TESTED LOCALLY for provisioning; actual rendered dashboard NOT VERIFIED**.
- [ ] Service availability changes when a monitored service is stopped/restarted. **TESTED LOCALLY for metrics/rules; visual Grafana outage/recovery NOT VERIFIED**.
- [ ] Request/error/latency panels show data after application traffic is generated. **IMPLEMENTED / TESTED LOCALLY for queries; traffic-filled rendered panels NOT VERIFIED**.
- [ ] Dashboard data matches Prometheus metrics. **TESTED LOCALLY for expression validity; rendered value comparison NOT VERIFIED**.
- [ ] No application credentials are displayed in dashboard panels. **TESTED LOCALLY for inspected static panel/query contents; live rendered privacy NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 2 — SCRUM-196: DO-203-Add GitHub Integration Operational Monitoring

Authoritative source: [SCRUM-196](https://researchtrack.atlassian.net/browse/SCRUM-196). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** GitHub regression 289 fresh passes, metric tests and real disposable business/receipt flow. Baseline CI GitHub isolated 3 passes. Provider HTTP is stubbed; no real daily reconciliation/panel acceptance.

**Final acceptance / missing proof:** Deployed success/failure/webhook/reconciliation metrics and panels, privacy review and approved controlled non-production integration failure.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] A successful GitHub synchronization changes the expected monitoring metrics. **TESTED LOCALLY with service tests/stubbed provider; deployed synchronization metrics NOT VERIFIED**.
- [ ] A controlled synchronization failure is visible in Prometheus/Grafana. **TESTED LOCALLY for failure metrics/rules; deployed Prometheus/Grafana failure display NOT VERIFIED**.
- [ ] Webhook processing activity is visible after a test webhook/event. **TESTED LOCALLY with real disposable webhook/business flow; authentic deployed webhook PENDING APPROVAL**.
- [ ] A failed webhook is distinguishable from a successful webhook. **TESTED LOCALLY for webhook outcomes; deployed distinguishability NOT VERIFIED**.
- [ ] Reconciliation execution is visible after the scheduled/manual test run. **NOT VERIFIED for deployed daily/manual reconciliation; configured counters/panels IMPLEMENTED**.
- [ ] No GitHub token, webhook secret or sensitive repository data appears in metrics. **TESTED LOCALLY for bounded metric labels; deployed scrape privacy NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 2 — SCRUM-197: DO-204-Configure Basic Monitoring Alerts and Failure Verification

Authoritative source: [SCRUM-197](https://researchtrack.atlassian.net/browse/SCRUM-197). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Rule tests preserve existing alert rules; local 33 scenarios pass, published CI has 30. Selected real fixture Kafka/processing alerts are not a complete Sprint 2 Test outage demonstration.

**Final acceptance / missing proof:** Test service-unavailable and GitHub failure alert firing/resolution, with redacted alert content and operational timestamps.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Stop a test backend service and confirm the monitoring state changes. **TESTED LOCALLY with deterministic service alert inputs; actual Sprint 2 backend stop NOT VERIFIED**.
- [ ] Restore the service and confirm recovery is reflected. **TESTED LOCALLY with deterministic rule recovery; actual Sprint 2 service recovery NOT VERIFIED**.
- [ ] Trigger a controlled GitHub synchronization failure and confirm the alert condition becomes visible. **TESTED LOCALLY with deterministic GitHub alert inputs; controlled sync-failure display NOT VERIFIED**.
- [ ] Verify recovery clears/resolves the test condition according to the configured behavior. **TESTED LOCALLY for rule recovery; actual Test condition resolution NOT VERIFIED**.
- [ ] Confirm alerts do not expose secrets. **TESTED LOCALLY for inspected rule label/annotation content; deployed content NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 3 — SCRUM-204: DO-301-Extend Prometheus and Grafana for Jira Integration Monitoring

Authoritative source: [SCRUM-204](https://researchtrack.atlassian.net/browse/SCRUM-204). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Jira regression 106 fresh passes, dashboard/rule checks and two real disposable Kafka/MySQL flows with business state. Provider HTTP is stubbed; no authenticated deployed Jira panels.

**Final acceptance / missing proof:** Live Jira health/webhook/reconciliation/sync panels, controlled non-production failure/recovery, and metric privacy.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Jira Service health is visible in Prometheus/Grafana. **IMPLEMENTED / TESTED LOCALLY for metric/query configuration; deployed panels NOT VERIFIED**.
- [ ] Test Jira webhook activity appears in monitoring. **TESTED LOCALLY with disposable Jira flow; authentic deployed webhook PENDING APPROVAL**.
- [ ] Successful Jira synchronization/reconciliation changes expected metrics. **TESTED LOCALLY for sync flow/metrics; deployed reconciliation metrics NOT VERIFIED**.
- [ ] Controlled Jira processing failure is visible. **TESTED LOCALLY for failure metric/rule behavior; deployed failure observation NOT VERIFIED**.
- [ ] Jira recovery is reflected by monitoring. **TESTED LOCALLY for selected fixture recovery/rule inputs; deployed Jira monitoring recovery NOT VERIFIED**.
- [ ] No Jira credentials or sensitive data are exposed. **TESTED LOCALLY for bounded labels/static queries; deployed scrape privacy NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 3 — SCRUM-205: DO-302-Provision Azure Production Infrastructure and Container Apps Environment

Authoritative source: [SCRUM-205](https://researchtrack.atlassian.net/browse/SCRUM-205). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Baseline CI Bicep and synthetic preflight/network/env tests pass. Infrastructure run 38153663507 succeeds with What-If/reconcile/private-network checks. Current effective cloud state is not independently inventoried.

**Final acceptance / missing proof:** Current resource/NSG/disk/ACA/private/public ingress/DNS/TLS/OIDC evidence and operational VPS Test proof.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Azure production resource group/resources are available. **DEPLOYED baseline infrastructure run; current effective resource inventory NOT VERIFIED**.
- [ ] Container Apps environment reports healthy/ready. **DEPLOYED baseline infrastructure/network validation; current environment state NOT VERIFIED**.
- [ ] External ingress reaches only intended public endpoints. **IMPLEMENTED / TESTED LOCALLY for ingress/NSG design; effective exposure/DNS/TLS NOT VERIFIED**.
- [ ] Internal service connectivity is available for intended services. **VERIFIED IN CI and baseline private-network validation; current connectivity NOT VERIFIED**.
- [ ] Persistent resources can be mounted/accessed by the intended workload where applicable. **DEPLOYED baseline mount/workload checks; current disk/mount identity NOT VERIFIED**.
- [ ] Production secrets/configuration can be supplied without source-code changes. **IMPLEMENTED / TESTED LOCALLY / VERIFIED IN CI; baseline production materialization succeeds**.
- [ ] VPS test environment remains operational. **NOT VERIFIED: current operational VPS Test acceptance not executed**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 3 — SCRUM-206: DO-303-Migrate ResearchTrack Production Services and Persistent Workloads to Azure

Authoritative source: [SCRUM-206](https://researchtrack.atlassian.net/browse/SCRUM-206). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Fresh seven backend regressions/build and existing env composition/rollback tests; five disposable Kafka/upgrade tests. Historical a26cfd2 rollouts, 1810f095 unchanged image retention. This backend audit has no current frontend/VPS Test acceptance.

**Final acceptance / missing proof:** Current seven app image/revision/schema/routing/persistence inventory, frontend → Gateway behavior, data survival and Test/old-VPS independence.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] All intended production services run in Azure. **DEPLOYED baseline backend releases; current frontend/all-service inventory NOT VERIFIED**.
- [ ] Frontend reaches the production API Gateway. **NOT VERIFIED: live frontend-to-Gateway acceptance outside this backend audit**.
- [ ] Gateway routes to required backend services. **TESTED LOCALLY in Gateway regressions; baseline private routing validated; current all-route acceptance NOT VERIFIED**.
- [ ] Backend services reach required databases/persistent resources. **VERIFIED IN CI / baseline migrations/connectivity; current DB/image/schema inventory NOT VERIFIED**.
- [ ] Health/readiness probes report the expected state. **TESTED LOCALLY including Healthy/Degraded contract; baseline health checks pass; public step skipped**.
- [ ] Selected service/container restart preserves required persistent data. **TESTED LOCALLY for disposable Kafka ordinary restart; complete production-workload restart acceptance NOT VERIFIED**.
- [ ] Production operates without depending on the previous VPS production deployment. **IMPLEMENTED Azure design; effective old-VPS production independence NOT VERIFIED**.
- [ ] VPS test remains functional. **NOT VERIFIED: current operational VPS Test acceptance not executed**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 3 — SCRUM-207: DO-304-Adapt CI/CD and Validate Azure Production Deployment

Authoritative source: [SCRUM-207](https://researchtrack.atlassian.net/browse/SCRUM-207). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** Fresh 34 workflow/25 policy/12 reconciliation tests; unchanged-source TASK 11 synthetic failure matrix. Required CI/Kafka and restoration/smoke succeed in published app/infra runs. Public readiness skipped; main remains an active competing route.

**Final acceptance / missing proof:** Reviewed local fixes with exact-SHA CI, current trusted deployment-source coordination, public contract and authorized compatible revision recovery.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Valid change passes CI and can deploy to the VPS test environment. **IMPLEMENTED Test path / TESTED LOCALLY with stubs; current VPS deployment NOT VERIFIED**.
- [ ] Production release deploys the expected image/version to Azure. **DEPLOYED baseline immutable image digests; local fixes/new main compatibility NOT VERIFIED**.
- [ ] Azure workloads become healthy after deployment. **VERIFIED IN CI and historical health steps; current state NOT VERIFIED**.
- [ ] Public production endpoint passes smoke verification. **NOT VERIFIED: explicit public endpoint check SKIPPED; k6 smoke success is separate evidence**.
- [ ] Selected service restart/revision rollout recovers successfully. **DEPLOYED historical a26cfd2 rollout; authorized compatible-revision rollback acceptance PENDING APPROVAL**.
- [ ] Failed verification is reported by the pipeline. **TESTED LOCALLY synthetic failure matrix; new local fixes not published**.
- [ ] Deployment credentials are not exposed in normal logs. **TESTED LOCALLY for inspected redaction/source handling; comprehensive deployed log privacy NOT VERIFIED**.
- [ ] Test deployment continues to target VPS. **IMPLEMENTED Test target and synthetic orchestration; current actual VPS Test release NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 4 — SCRUM-201: DO-401 - Deploy and Configure Kafka Infrastructure

Authoritative source: [SCRUM-201](https://researchtrack.atlassian.net/browse/SCRUM-201). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** 62 offline operation assertions + 8 Python methods fresh PASS; topics/config 21 fresh PASS; real disposable TLS Kafka startup/restart in TASK 09 and baseline CI. Previous mount cause is not confirmed by a live mount dump.

**Final acceptance / missing proof:** Read-only deployed mount/cluster identity/CA/advertised listener/retention/NSG, ACA-private and exporter evidence. Single-node/RF1 is not automatic failover.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Kafka starts successfully. **TESTED LOCALLY real fixture / VERIFIED IN CI for baseline isolated startup; current live broker NOT VERIFIED**.
- [ ] Intended ResearchTrack services can resolve/connect to Kafka. **TESTED LOCALLY for real GitHub/Jira TLS clients; baseline private probe succeeds; current ACA path NOT VERIFIED**.
- [ ] Basic producer/consumer smoke test succeeds. **TESTED LOCALLY with real producer/consumer and baseline VERIFIED IN CI; deployed provider activity PENDING APPROVAL**.
- [ ] Kafka restarts without recreating the entire ResearchTrack environment. **TESTED LOCALLY ordinary disposable broker restart, retained records/offsets; production restart NOT EXECUTED**.
- [ ] Required runtime configuration is externalized. **IMPLEMENTED / TESTED LOCALLY / VERIFIED IN CI; effective deployed values NOT VERIFIED**.
- [ ] Kafka is not exposed publicly without need. **IMPLEMENTED / TESTED LOCALLY private listener/NSG configuration; current effective exposure NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 4 — SCRUM-202: DO-402 - Configure Kafka Topics and Integrate ResearchTrack Event Flows

Authoritative source: [SCRUM-202](https://researchtrack.atlassian.net/browse/SCRUM-202). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** GitHub/Jira messaging 15/15 fresh; atomic rollback/dedup/identity tests in 289/106 fresh service regressions; TASK 09 real broker + MySQL 3/2 PASS, source hashes unchanged. Baseline CI 3/2 PASS covers older published code.

**Final acceptance / missing proof:** Authentic approved provider actions → retained webhook/outbox → Kafka receipt/offset → final business rows, deployed topic/CA/image evidence and restart without manual topic recreation.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Required topics exist. **IMPLEMENTED manifest / TESTED LOCALLY real fixture topics; current approved live settings NOT VERIFIED**.
- [ ] GitHub-related producer can publish an agreed test event. **TESTED LOCALLY real GitHub publication; baseline VERIFIED IN CI; authentic deployed event PENDING APPROVAL**.
- [ ] Intended consumer receives/processes the test event. **TESTED LOCALLY real GitHub consumer/receipt/business DB with provider stub; deployed outcome PENDING APPROVAL**.
- [ ] Jira-related producer can publish an agreed test event. **TESTED LOCALLY real Jira publication; baseline VERIFIED IN CI; authentic deployed event PENDING APPROVAL**.
- [ ] Intended consumer receives/processes the test event. **TESTED LOCALLY real Jira consumer/receipt/business DB with provider stub; deployed outcome PENDING APPROVAL**.
- [ ] Restarting an application service does not require manual topic recreation. **NOT VERIFIED for full application process restart; consumer rejoin and ordinary broker restart tested locally**.
- [ ] Environment-specific Kafka values remain externalized. **IMPLEMENTED / TESTED LOCALLY / VERIFIED IN CI for external configuration; effective live settings NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 4 — SCRUM-203: DO-403 - Implement Kafka Observability and Event Processing Alerts

Authoritative source: [SCRUM-203](https://researchtrack.atlassian.net/browse/SCRUM-203). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** TASK 08/12 local 159 checks/33 scenarios/55 expressions, nine metrics tests and 21 operator queries; TASK 09 selected real scrape/fault transitions. Published baseline CI ran 159/30/55, not local 33.

**Final acceptance / missing proof:** Current deployed GitHub/Jira metric scrapes, exporter/targets/datasource/panels, bounded labels, and operational observation of approved events; no production outages.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Kafka availability is visible in Prometheus/Grafana. **TESTED LOCALLY real Prometheus scrape and dashboard queries; rendered/deployed Grafana NOT VERIFIED**.
- [ ] Broker outage changes the expected monitoring state. **TESTED LOCALLY selected disposable outage alert transitions; no production outage executed**.
- [ ] Event-processing activity is visible after test events. **TESTED LOCALLY disposable event metrics/scrape; deployed provider/metric activity PENDING APPROVAL**.
- [ ] Controlled processing failure is distinguishable from success where metrics support it. **TESTED LOCALLY selected actual failure metrics and deterministic rules; all real fault types NOT VERIFIED**.
- [ ] Relevant alert conditions can be demonstrated. **TESTED LOCALLY all ten deterministic alerts/recovery and selected real transitions; deployed observations NOT VERIFIED**.
- [ ] No message payload/credential is exposed. **TESTED LOCALLY bounded labels/static panels; deployed scrape privacy NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Sprint 4 — SCRUM-208: DO-404 - Validate Kafka Failure Recovery and Final Event-Driven Deployment

Authoritative source: [SCRUM-208](https://researchtrack.atlassian.net/browse/SCRUM-208). Jira currently Done; this does not close acceptance evidence gaps.

**Available evidence:** TASK 09 fixture cfa1267c500d five PASS, zero skips: real outage, commit failure/rejoin, durable dedup/business, ordinary 128-record restart and selected readiness/metrics/alerts. All recorded source hashes match. Baseline CI five PASS precedes extended local assertions.

**Final acceptance / missing proof:** Final deployed authentic-provider/readiness/business/offset/monitoring acceptance; narrower transport exhaustion, distinct-event batches, Jira held batch and crash/disk cases remain NOT VERIFIED per TASK 09 matrix.

Original verification criteria with evidence classification. Open boxes retain final owner acceptance/sign-off; TESTED LOCALLY and baseline CI evidence do not establish every deployed target:

- [ ] Agreed GitHub event is processed end to end. **TESTED LOCALLY real Kafka/MySQL and actual business worker with provider stub; authentic deployed acceptance PENDING APPROVAL**.
- [ ] Agreed Jira event is processed end to end. **TESTED LOCALLY real Kafka/MySQL and actual business worker with provider stub; authentic deployed acceptance PENDING APPROVAL**.
- [ ] Kafka outage is detected by monitoring. **TESTED LOCALLY selected broker/exporter alert transitions; no production chaos required**.
- [ ] Kafka restart restores broker availability. **TESTED LOCALLY ordinary disposable restart with identity/log/group continuity**.
- [ ] Configured services reconnect/recover according to implementation. **TESTED LOCALLY reconnect, scheduled retry, commit-failure rejoin and dedup; full long exhaustion schedule NOT VERIFIED**.
- [ ] Unrelated ResearchTrack functionality remains available where expected. **TESTED LOCALLY GitHub/Jira liveness/Swagger/readiness; all other business HTTP routes during outage NOT VERIFIED**.
- [ ] Final smoke test passes after recovery. **TESTED LOCALLY fixture final topic/group/health/business postflight; final deployed provider acceptance NOT VERIFIED**.
- [ ] Recovery steps are reproducible. **IMPLEMENTED runbook / TESTED LOCALLY fixture reproduction; full DR restore NOT VERIFIED**.

- [ ] Attach redacted operational evidence and reviewer sign-off for all original scope/DoD items; record each item as IMPLEMENTED, TESTED LOCALLY, VERIFIED IN CI, DEPLOYED, VERIFIED IN PRODUCTION, PENDING APPROVAL or NOT VERIFIED.

## Cross-cutting final gates

- [ ] Capture current Azure resource/NSG/disk/mount/CA/listener/private-ACA/image/revision/schema evidence, using read-only report procedures.
- [ ] Inventory actual OIDC issuer/immutable subject/audience and effective roles; preserve the existing identity/federation.
- [ ] Verify public DNS/TLS and response contract before separately approving `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true`; 200 Degraded/Gateway-only health is not full dependency health.
- [ ] Verify authenticated Grafana dashboards, current Prometheus targets and deployed GitHub/Jira Kafka metrics, with no sensitive labels.
- [ ] Keep chaos tests disposable; do not require production outage injection to fill evidence gaps.
- [ ] Verify current VPS Test and frontend behavior; this backend audit did not execute live frontend/VPS acceptance.
- [ ] Obtain representative Jira Issues latency-stage evidence and owner-agreed endpoint SLO; retain existing thresholds until supported change review.
- [ ] Verify compatible revision rollback and exact runtime restoration with approved evidence; no destructive schema rollback, offset reset or pending-event clearing.
- [ ] Verify independent encrypted backup retrieval/decryption and freshness, full matched DB/Kafka/application restore and approved measured RTO/RPO; local seven-file backups/RF1 are not DR.
- [ ] Review raw actionlint queue warnings and publish/test the selective Node24 tooling changes; monitoring image pinning waits for actual deployed identities.
- [ ] Review/merge through the approved main process, coordinate source policy/triggers atomically, validate exact-SHA main release and only then retire the temporary deployment route. Nothing is switched automatically.
- [ ] Owner accepts or explicitly defers each remaining risk with evidence scope, responsible person and target date.

Local review material is complete; production/story acceptance evidence remains incomplete.

**BLOCKED — ACCEPTANCE EVIDENCE MISSING**
