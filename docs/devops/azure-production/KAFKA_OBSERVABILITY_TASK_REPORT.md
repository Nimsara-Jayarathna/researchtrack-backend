# Sprint 4 Kafka observability — local implementation audit

2026-10-11, branch `devops/sprint4-kafka-infrastructure-verification`, unchanged HEAD `6911df78db0533ac6bea2387f9363e1d46d0def4`. Scope: local code/configuration/documentation and offline tests. Production visibility and alert transitions are not verified. No actual environment credentials were inspected or inserted.

## A. Existing architecture

Existing Azure VM Docker network with Kafka 3.9.1, MySQL, Nginx, Prometheus and Grafana; GitHub/Jira Container Apps already expose private metrics and run transactional webhook outboxes, receipt deduplication and existing synchronization workers. External private Kafka SSL 9092, internal Docker plaintext 29092, persistent Kafka identity/data and approved v1 topic contracts are retained.

## B. Original gaps

No broker exporter/scrape, Kafka worker metrics, bounded outbox sampling, Kafka dashboard, Kafka alert group, deterministic Kafka alert tests or monitoring-only deployment/verification path existed. Existing GitHub/Jira operational metrics and dashboards were retained rather than replaced.

## C. Implemented changes

- Pinned private exporter (v1.10.0 plus OCI digest), topic/group filters, no host port/credential mount, restricted privileges and resource budget.
- Broker health uses HTTP UP **plus** broker count **plus** live smoke-topic offset; cached broker count alone is insufficient.
- Publication/retry/duration, durable handoff/duplicate/noop/failure, independent offset commit, held state, configured enablement and bounded cached outbox metrics. Success is not remote business-sync completion.
- 18-panel Kafka dashboard, two overview additions, ten alerts and one recording rule; all original panels/rules/services preserved.
- Required production scrape and explicitly optional legacy Test target (Test has no Kafka exporter). Disabled app alerts are gated; production broker monitoring remains independent.
- Monitoring-only manual workflow input, bundle/installer/reconciliation, existing deployed monitoring image IDs, configuration backups and unchanged broker/database/edge container identity verification. Automatic triggers/default full deployment path unchanged.
- Isolated integration extension and manual artifact upload; runtime execution remains pending Docker availability.
- Azure empty-CA correction from the preceding request: only disabled/empty CA is omitted from app/job env and secrets; provided CA remains secretRef; enabled production requires a real valid Base64 public CA. Supplied certificates are validated even when disabled, so invalid placeholders/private keys fail rather than becoming CA secrets. Other required empty secrets and duplicate keys still fail. No SSH private key was generated/used as CA; synthetic certificate-authority keys are confined to disposable test certificate generation.

## D. Exact modified/new files

The complete inventory appears at the end of this report. Generated evidence remains ignored under `artifacts/` and is not part of the source inventory.

## E. Metrics and exact PromQL

[The runbook](KAFKA_OBSERVABILITY.md#metric-semantics-and-privacy) documents all ten application families and their bounded labels. [Exact dashboard expressions](KAFKA_OBSERVABILITY.md#exact-kafka-dashboard-promql) are copied from JSON; all 55 expressions across all four dashboards are parsed by real promtool. Exporter families used are `kafka_brokers`, `kafka_topic_partitions`, `kafka_topic_partition_current_offset`, `kafka_consumergroup_members`, `kafka_consumergroup_current_offset` and `kafka_consumergroup_lag`; the recording rule is `researchtrack_kafka_broker_healthy`. No JVM/CPU/throughput metrics are invented.

Useful direct queries:

```promql
researchtrack_kafka_broker_healthy{environment="production"}
up{job="researchtrack-kafka",environment="production"}
sum by(service,outcome) (rate(researchtrack_kafka_publish_total{environment="production"}[5m]))
sum by(service,outcome) (increase(researchtrack_kafka_consume_total{environment="production",stage="process"}[10m]))
sum by(service,outcome) (increase(researchtrack_kafka_consume_total{environment="production",stage="commit"}[10m]))
researchtrack_kafka_consumer_held{environment="production"}
ALERTS{category="kafka",alertstate="firing"}
```

## F. Grafana

File: `deploy/monitoring/grafana/provisioning/dashboards/json/kafka-observability.json`; UID `researchtrack-kafka-observability`; datasource `prometheus`; existing ResearchTrack folder; title **ResearchTrack — Kafka & Event Processing**. Access via existing `https://<GRAFANA_HOSTNAME>/d/researchtrack-kafka-observability`, with existing operator login. Production provisioning/UI inspection pending.

## G. Alert names and thresholds

| Alert | Condition / stabilization |
| --- | --- |
| `ResearchTrackKafkaExporterUnavailable` | Required target UP=0 or job absent / 2m critical |
| `ResearchTrackKafkaBrokerUnavailable` | Exporter UP but health evidence missing/invalid / 2m critical |
| `ResearchTrackKafkaPublishingFailures` | >=3 failed attempts in 10m / 2m warning |
| `ResearchTrackKafkaEventProcessingFailures` | >=3 failed/rejected attempts in 10m / 2m warning |
| `ResearchTrackKafkaOffsetCommitFailures` | >=3 failed commits in 10m / 2m warning |
| `ResearchTrackKafkaConsumerHeld` | Held=1 / 1m critical |
| `ResearchTrackKafkaConsumerLagHigh` | Known committed offsets, measured approved group lag >100 / 10m warning |
| `ResearchTrackKafkaOutboxBacklog` | Fresh pending/PUBLISHING depth >=100 / 5m warning |
| `ResearchTrackKafkaOutboxFailed` | Fresh failed depth >0 / 2m warning |
| `ResearchTrackKafkaOutboxMetricsUnavailable` | Sampling failed/absent or >=180s stale / 5m warning |

App/group/outbox conditions require enabled=1; broker monitoring is independent. Counters resolve after the rolling window ages out; measured states resolve after repair/drain/fresh sample. Alerts are visible in Prometheus/Grafana; no external notification platform/verified delivery route is added.

## H. Commands and actual results

| Executed check | Actual result |
| --- | --- |
| `dotnet build ResearchTrack.sln -c Release --no-restore --verbosity minimal` | PASS, 0 warnings, 0 errors |
| `dotnet test <each of seven test projects> -c Release --no-build --filter 'Category!=DatabaseIntegration' --logger 'trx;LogFileName=tests.trx' --results-directory artifacts/kafka-observability/full-suite/<project>` | **369 passed, 0 failed, 3 skipped**; separate TRX retained per assembly |
| New `KafkaMetricsTests` | 9 passing facts in full suite: acknowledgment boundary, retry/permanent failure, failure bookkeeping outage, durable deduplication, commit distinction, handler retries/failures, held/invalid record, cached sampling/privacy, disabled mode |
| `PROMTOOL=<official native 3.15.0 promtool> scripts/test-kafka-observability.sh` | **158 configuration assertions/checks**, **30 deterministic Prometheus scenarios**, **55 dashboard expressions parsed**; both VM/Test Compose syntax; Azure/Test/isolated Prometheus configs; 26 rules |
| `python3 deploy/azure/validation/test_monitoring_reconciliation.py` | **10 passed**; real script + mocked Docker; workflow show/create behavior and unchanged automatic trigger contract |
| `python3 deploy/azure/validation/test_kafka_topics_config.py` | **21 passed**; disabled/empty, enabled/empty, enabled/valid for apps/jobs; private material rejected; other required secrets and duplicates still rejected |
| `deploy/azure/validation/test-validate-azure-env-files.sh` | **22 checks passed** |
| `deploy/azure/validation/test-service-env-composition.sh` | **10 checks passed**, including migration job optional-CA omission and shared JWT failures |
| `deploy/azure/validation/test-kafka-operations.sh` | **43 cases passed**, including bundle contents/modes and preservation safety |
| `deploy/azure/validation/test-kafka-smoke.sh` | **9 cases passed**, stubbed Kafka only |
| `deploy/azure/validation/test-network-probe.sh` | **12 checks passed**, stubbed Azure/Kafka |
| `deploy/azure/validation/test-deploy-image-selection.sh` | **11 checks passed**, stubbed Azure/GHCR |
| Changed shell/Python syntax; `git diff --check` | PASS |
| `shellcheck --severity=warning <changed shell files>` | PASS; pre-existing informational/style findings remain |
| `actionlint` Backend CI/manual integration | PASS |
| Azure infrastructure `actionlint` | Installed version rejects the **pre-existing** `concurrency.queue: max`. Temporary lint copy omitting only that unchanged key passes. Repository concurrency remains unchanged; native full-file lint limitation is recorded, not hidden. |
| `scripts/test-kafka-integration.sh` | **NOT EXECUTED**, exit 2: Docker daemon unavailable; the three conditional native Kafka/MySQL cases were skipped in unit suite |
| Azure deployment / live production validation / Grafana screenshots | **NOT EXECUTED**, not authorized by this task |

Per-assembly passing totals: Auth 15, Gateway 24, GitHub 205 (+2 skipped), Jira 61 (+1 skipped), Meeting 27, Project 9, Submission 28. Evidence: `artifacts/kafka-observability/verification-summary.json`, `configuration-results.log`, `reconciliation-results.log`, `full-suite/test-results.log` and seven per-project TRX files. These artifacts contain synthetic/local evidence, never production credentials.

The isolated extension verifies actual exporter/Prometheus, GH/Jira publication/durable handling, a native publication timeout, actual processing failures/held state, 128-record measured group lag, firing alerts and recovery. It serves actual registry snapshots rather than deployed ASP.NET endpoints. Shortened fixture alert durations do not establish production timing: production timing is exercised in promtool scenarios. The broker failure simulation is restricted to the unique disposable fixture. Execution evidence for these new live assertions is **pending**, not claimed.

## I. Production deployment steps

1. Review the diff and run the isolated runner with Docker. Plan release handling around the branch's existing automatic deployment triggers before pushing anything.
2. Once reviewed code is on the authorized release ref, manually dispatch existing **Azure Infrastructure - Production** with `monitoring_only=true`, `allow_destructive=false` under the existing protected production environment/concurrency lock.
3. It reads existing outputs and requires a running VM; no Bicep creation, VM setup, runtime secret materialization or network-probe jobs occur. It backs up configuration and validates Compose/Prometheus before applying exporter/monitoring changes only. Existing Kafka/MySQL/Nginx identities/start times are checked.
4. Separately release GitHub/Jira images through the existing app pipeline when authorized; monitoring-only does not deploy application metrics code. Keep current enablement/TLS/contracts. Production enablement still requires the genuine public CA.
5. Run the read-only monitoring verification checklist and retain sanitized evidence. See [full delivery procedure](KAFKA_OBSERVABILITY.md#production-delivery-after-review-and-separate-authorization).

## J. Production verification checklist

Verify pinned exporter/no public 9308 or secret mounts; unchanged broker/database/edge IDs/start times/TLS/storage/topics; exporter target UP with semantic labels; broker count and live smoke offset; health rule=1; all existing/new rules; datasource and four dashboards; private app metrics and ordinary traffic/duplicate/commit/business outcomes after image rollout; fresh cached sampling; meaningful group offsets/lag/no-data handling; configured disabled gating. Confirm existing webhook/auth/sync/migrations remain healthy. Use isolated/deterministic tests for manufactured outages; never simulate a production failure. Full evidence checklist is in the runbook.

## K. Rollback

Under normal release authorization/concurrency protection, restore recorded backup Compose, validation script, Prometheus template/rules and Grafana provisioning in place. Check restored config with current promtool, recreate only Prometheus/Grafana with `--no-deps --pull never` and current image IDs, and stop/remove only the exporter if reverting its introduction. Never run full stack down, delete volumes, recreate Kafka data/identity/TLS, reset offsets or undo DB migrations. Instrumentation requires no database migration/rollback. File restoration after failed preflight is explicit; installer does not automatically roll back. See [recovery and rollback](KAFKA_OBSERVABILITY.md#troubleshooting-recovery-and-rollback).

## L. Remaining blockers/limitations

- Docker daemon unavailable: actual isolated broker/exporter/MySQL/Prometheus scenarios must run before release approval.
- Production deployment and live target/dashboard/ordinary event/alert-state verification require a separately authorized release; no screenshots/live claims supplied.
- GitHub/Jira application metrics need reviewed image rollout. Existing disabled configurations stay disabled; valid CA and approved contracts are prerequisites for any future enablement.
- Existing Test stack does not supply Kafka/exporter; target is optional until privately supplied. No external notifications are verified. Exporter supplies metadata/offsets, not JMX CPU/heap metrics.
- Installed Actionlint does not understand the unchanged baseline concurrency queue key; remaining workflow syntax and mocked control flow are verified.

## M. Final seven-item Jira Definition of Done

| Item | Status | Files / executed tests / evidence | Live verification / remaining work |
| --- | --- | --- | --- |
| 1. Kafka operational health is visible | IMPLEMENTED — PRODUCTION VERIFICATION PENDING | VM Compose + both Prometheus configs + health rule + VM validator; 158 config checks, 30 alert scenarios, 10 reconciliation tests; local logs | No live scrape. Run isolated then production target/count/offset checks. |
| 2. Event-processing indicators available where supported | IMPLEMENTED — PRODUCTION VERIFICATION PENDING | `KafkaMetrics.cs`, workers/persistence, both durable handlers and metrics/flow tests; 369 passing tests including 9 new metrics facts; TRX | Live GH/Jira flow extension unexecuted; release images, inspect private endpoints and ordinary event outcomes. |
| 3. Kafka Grafana panels integrated | IMPLEMENTED — PRODUCTION VERIFICATION PENDING | New dashboard + two overview panels; JSON/UID/datasource/provisioning/original panel tests; 55 parsed expressions | Provision dashboard through existing Grafana and capture sanitized screenshots. |
| 4. Broker outage alert configured | IMPLEMENTED — PRODUCTION VERIFICATION PENDING | Separate rules group + fixtures + fixture verifier + VM validator; required/absent/cached/negative broker evidence, exporter failures and recovery tests pass | Disposable real outage run pending; production loaded rule and natural alert state verification pending. No production failure simulation. |
| 5. Relevant event-processing alerts configured | IMPLEMENTED — PRODUCTION VERIFICATION PENDING | Ten new alerts, actual worker metrics, enabled/freshness/offset gates; repeated failure, held, lag, backlog, stale sample and recovery scenarios pass | Real fixture failure/lag transitions pending Docker; loaded production alerts/ordinary recovery evidence pending. |
| 6. Sensitive event data excluded | COMPLETE | Bounded metric methods/labels, actual handler registry exports, filtered exporter target/group configuration, no key mounts; privacy assertions in 9 metrics facts and Jira deduplication test pass | Confirm deployed endpoint exposure and sanitized labels during release verification; no live privacy scan claimed. |
| 7. Monitoring documentation updated | COMPLETE | Runbook, this audit, monitoring README, Azure VM/index/status docs and both Sprint 4 Kafka reports; exact queries/commands/thresholds/delivery/rollback documented | Record live evidence after separately approved deployment. |

## N. No publication/deployment performed

No stage, commit, push, merge, workflow dispatch, Azure deployment/write, GitHub Environment secret change, production restart, production failure simulation, production topic/data change or frontend edit was performed. HEAD remains `6911df78`. Existing database migration and environment-template files are unchanged. All source changes remain local for review.

## Complete source inventory

| Status | File |
| --- | --- |
| Modified | [.github/workflows/azure-infrastructure.yml](../../../.github/workflows/azure-infrastructure.yml) |
| Modified | [.github/workflows/backend-ci.yml](../../../.github/workflows/backend-ci.yml) |
| Modified | [.github/workflows/kafka-integration-tests.yml](../../../.github/workflows/kafka-integration-tests.yml) |
| Modified | [deploy/azure/scripts/build-vm-bundle.sh](../../../deploy/azure/scripts/build-vm-bundle.sh) |
| New | [deploy/azure/scripts/install-monitoring-bundle.sh](../../../deploy/azure/scripts/install-monitoring-bundle.sh) |
| Modified | [deploy/azure/scripts/kafka_config.py](../../../deploy/azure/scripts/kafka_config.py) |
| Modified | [deploy/azure/scripts/render-containerapp.py](../../../deploy/azure/scripts/render-containerapp.py) |
| Modified | [deploy/azure/scripts/validate-vm-stack.sh](../../../deploy/azure/scripts/validate-vm-stack.sh) |
| Modified | [deploy/azure/validation/test-service-env-composition.sh](../../../deploy/azure/validation/test-service-env-composition.sh) |
| Modified | [deploy/azure/validation/test-validate-azure-env-files.sh](../../../deploy/azure/validation/test-validate-azure-env-files.sh) |
| Modified | [deploy/azure/validation/test_kafka_topics_config.py](../../../deploy/azure/validation/test_kafka_topics_config.py) |
| New | [deploy/azure/validation/test_monitoring_reconciliation.py](../../../deploy/azure/validation/test_monitoring_reconciliation.py) |
| Modified | [deploy/azure/vm/compose.yml](../../../deploy/azure/vm/compose.yml) |
| Modified | [deploy/azure/vm/prometheus/prometheus.yml.template](../../../deploy/azure/vm/prometheus/prometheus.yml.template) |
| New | [deploy/azure/vm/scripts/reconcile-monitoring.sh](../../../deploy/azure/vm/scripts/reconcile-monitoring.sh) |
| Modified | [deploy/azure/vm/scripts/reconcile-stack.sh](../../../deploy/azure/vm/scripts/reconcile-stack.sh) |
| Modified | [deploy/monitoring/README.md](../../../deploy/monitoring/README.md) |
| New | [deploy/monitoring/grafana/provisioning/dashboards/json/kafka-observability.json](../../../deploy/monitoring/grafana/provisioning/dashboards/json/kafka-observability.json) |
| Modified | [deploy/monitoring/grafana/provisioning/dashboards/json/researchtrack-overview.json](../../../deploy/monitoring/grafana/provisioning/dashboards/json/researchtrack-overview.json) |
| Modified | [deploy/monitoring/prometheus.yml](../../../deploy/monitoring/prometheus.yml) |
| Modified | [deploy/monitoring/rules/researchtrack-alerts.yml](../../../deploy/monitoring/rules/researchtrack-alerts.yml) |
| New | [deploy/monitoring/tests/kafka-alerts.test.yml](../../../deploy/monitoring/tests/kafka-alerts.test.yml) |
| New | [deploy/monitoring/tests/validate_kafka_observability.py](../../../deploy/monitoring/tests/validate_kafka_observability.py) |
| Modified | [docs/devops/azure-production/02_INFRASTRUCTURE_VM.md](../../../docs/devops/azure-production/02_INFRASTRUCTURE_VM.md) |
| Modified | [docs/devops/azure-production/IMPLEMENTATION_STATUS.md](../../../docs/devops/azure-production/IMPLEMENTATION_STATUS.md) |
| New | [docs/devops/azure-production/KAFKA_OBSERVABILITY.md](../../../docs/devops/azure-production/KAFKA_OBSERVABILITY.md) |
| New | [docs/devops/azure-production/KAFKA_OBSERVABILITY_TASK_REPORT.md](../../../docs/devops/azure-production/KAFKA_OBSERVABILITY_TASK_REPORT.md) |
| Modified | [docs/devops/azure-production/KAFKA_TASK_REPORT.md](../../../docs/devops/azure-production/KAFKA_TASK_REPORT.md) |
| Modified | [docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md](../../../docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md) |
| Modified | [docs/devops/azure-production/README.md](../../../docs/devops/azure-production/README.md) |
| Modified | [scripts/test-kafka-integration.sh](../../../scripts/test-kafka-integration.sh) |
| New | [scripts/test-kafka-observability.sh](../../../scripts/test-kafka-observability.sh) |
| Modified | [src/BuildingBlocks/Kafka/KafkaMessagingWorkers.cs](../../../src/BuildingBlocks/Kafka/KafkaMessagingWorkers.cs) |
| New | [src/BuildingBlocks/Kafka/KafkaMetrics.cs](../../../src/BuildingBlocks/Kafka/KafkaMetrics.cs) |
| Modified | [src/BuildingBlocks/Kafka/KafkaPersistence.cs](../../../src/BuildingBlocks/Kafka/KafkaPersistence.cs) |
| Modified | [src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubKafkaEventHandler.cs](../../../src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubKafkaEventHandler.cs) |
| Modified | [src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs](../../../src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs) |
| Modified | [tests/Kafka/IsolatedKafkaFixture.cs](../../../tests/Kafka/IsolatedKafkaFixture.cs) |
| New | [tests/Kafka/KafkaMetricSnapshot.cs](../../../tests/Kafka/KafkaMetricSnapshot.cs) |
| Modified | [tests/Kafka/compose.yml](../../../tests/Kafka/compose.yml) |
| New | [tests/Kafka/prometheus.yml](../../../tests/Kafka/prometheus.yml) |
| New | [tests/Kafka/verify_observability.py](../../../tests/Kafka/verify_observability.py) |
| Modified | [tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs](../../../tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs) |
| New | [tests/ResearchTrack.GitHubService.Tests/KafkaMetricsTests.cs](../../../tests/ResearchTrack.GitHubService.Tests/KafkaMetricsTests.cs) |
| Modified | [tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj](../../../tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj) |
| Modified | [tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs](../../../tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs) |
| Modified | [tests/ResearchTrack.JiraService.Tests/KafkaMessagingTests.cs](../../../tests/ResearchTrack.JiraService.Tests/KafkaMessagingTests.cs) |
| Modified | [tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj](../../../tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj) |
