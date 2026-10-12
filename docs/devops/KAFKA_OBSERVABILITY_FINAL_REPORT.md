# TASK 08 — Kafka observability and Grafana verification

Story: **DO-403 / SCRUM-203**. Audit date: **2026-10-12** (Asia/Colombo).
Repository: `Nimsara-Jayarathna/researchtrack-backend`.
Working branch: `integration/main-kafka-production-20261011`.
Base HEAD: `1810f09535d6a6d00947bcf84d18fde473e40c39`, with the existing uncommitted TASK 02–07 changes preserved.

## Result and evidence boundaries

Repository configuration, dashboard expressions, all ten Kafka alerts, and disposable failure/recovery testing pass. Three missing alert-recovery scenarios were added. No production rule, dashboard, metric implementation, workflow, credential or resource was changed.

| Status | Meaning | TASK 08 finding |
| --- | --- | --- |
| Configured | Present in reviewed repository files | Exporter, scrape targets, metrics, four dashboards, ten Kafka alerts and recording rule are configured. |
| Locally tested | Executed against synthetic inputs or a disposable Docker project | PASS: 159 configuration checks, 33 Prometheus scenarios, 55 dashboard expressions, nine metric tests, 12 monitoring reconciliation tests and five real Kafka/MySQL integration tests. |
| Deployed | The running image/configuration is identified on the production target | **Not established in this task.** Repository files and earlier deployment discussions do not establish current production state. |
| Verified live | Timestamped observations from the deployed production target | **NOT EXECUTED.** Requires separate authorization for the read-only procedure below. |
| Not verified | Evidence not obtained or not exercised by this test environment | Production scrape/TLS/metric presence, Grafana rendering/access, deployed rule evaluation and notification delivery. |

The final isolated broker is healthy and both groups have measured lag zero. This is **not** a claim that every alert is clear: the GitHub processing-failure alert remains within its rolling ten-minute window in final fixture evidence. Deterministic rule tests independently prove its recovery after that window. No production outages or events were generated.

## Implementation inspected

| Requirement | Implementation and verified interpretation | Local proof |
| --- | --- | --- |
| 1. Broker metrics | Exporter supplies broker count, topic partitions, partition offsets and group offsets/lag. `researchtrack_kafka_broker_healthy` requires scrape UP, broker count >=1 and a nonnegative read of existing smoke partition 0. Cached broker count alone cannot prove connectivity. This is Kafka metadata/offset monitoring, not JMX heap/CPU/throughput monitoring. | Broker-zero, absent metadata, absent/negative offset and broker-recovery rule cases; real outage/recovery. |
| 2. Exporter scrape | Production `researchtrack-kafka` targets `kafka-exporter:9308` every 30s with a 10s timeout; labels `service=kafka`, `environment=production`. Image is v1.10.0 pinned by digest. | Compose security checks; real Prometheus target and recording-rule checks. |
| 3. Producer failures | `researchtrack_kafka_publish_total{outcome="retryable_failure"/"permanent_failure"}` increments on failed publication iterations, including failure-bookkeeping exceptions. Success follows broker acknowledgment **and** outbox bookkeeping. | Metric tests and real producer timeout/retry. |
| 4. Consumer failures | `researchtrack_kafka_consume_total{stage="process",outcome="failed"/"rejected"}` distinguishes failed handoff attempts from invalid contracts. `success` means committed durable receipt/handoff, not completed provider synchronization. | Failure/retry/contract metric tests and real held-consumer alert scenario. |
| 5. Consumer lag | Approved groups/topics only. Lag is considered measured only with a nonnegative committed offset; absent groups and unknown offsets remain no-data. | Negative/missing-offset cases; real GitHub lag >=128 followed by zero. |
| 6. Offset commit failures | `stage="commit",outcome="failed"` is separate from processing. Worker closes/rejoins automatically, replays through receipt deduplication, then commits the exact next offset. | Metric test, real GitHub/Jira commit-failure recovery and new rolling-window rule test. |
| 7. Outbox backlog | Cached `status="pending"` includes PENDING and active PUBLISHING leases. Counts require successful sampling and sample age <180s. | Cached-sampling test; backlog, stale/failed sampler and recovery cases. |
| 8. Failed messages | `status="failed"` measures retained permanently failed/exhausted outbox rows, not Kafka dead-letter records. No new DLQ/replay path is introduced. | Permanent failure metric/rule cases and new fresh-sample recovery case. |
| 9. Retries | `researchtrack_kafka_retry_total{stage="publish"/"process"}` counts actual additional attempts. Commit failures have their own counter; no commit-retry series is invented. | Publication/process retry tests; actual producer recovery retry=1. |
| 10. Held consumers | `researchtrack_kafka_consumer_held=1` means held without offset progress. Healthy transient commit recovery does not hold the consumer. A fresh registry models the existing reviewed application restart for held recovery. | Bounded-label/contract tests; real held and lag firing/recovery. |
| 11. Dashboards | Four unique dashboard UIDs, fixed `prometheus` datasource UID, file provisioning; Kafka dashboard has 18 panels. All 55 distinct provisioned expressions parse with promtool. Kafka metric-family references match application/exporter families. | Existing validator. Parsing does not establish production data, Grafana version compatibility or browser rendering. |
| 12–14. Ten alerts, firing and recovery | Production expressions and `for` durations tested by promtool. Coverage gaps for commit failures, failed outbox and sampler recovery closed here. | 33 synthetic scenarios plus selected real fixture transitions; matrix below. |
| 15. Cardinality/privacy | Kafka custom labels are bounded service/outcome/stage/status/histogram bucket labels; no event, tenant, project, repository, issue, user, payload, URL, credential or exception-text labels. Exporter filters only approved topics/groups and drops `kafka_broker_info`. | Label assertions and sensitive sentinel checks; live label inventory still required. |

Inspected sources:

- `src/BuildingBlocks/Kafka/KafkaMetrics.cs`, `KafkaMessagingWorkers.cs` and `KafkaPersistence.cs`; both service event handlers and existing metric endpoints.
- `deploy/monitoring/prometheus.yml`, `deploy/azure/vm/prometheus/prometheus.yml.template`, `deploy/azure/vm/compose.yml`.
- `deploy/monitoring/rules/researchtrack-alerts.yml`, `deploy/monitoring/tests/kafka-alerts.test.yml`, `validate_kafka_observability.py` and `scripts/test-kafka-observability.sh`.
- All dashboard JSON, datasource/dashboard provisioning, `tests/Kafka/`, `KafkaMetricsTests.cs`, GitHub/Jira flow tests and monitoring reconciliation/validation scripts.

## Scraping, privacy and operational limits

The production exporter reaches `kafka:29092` on the existing Docker-private plaintext listener. It has no published port, TLS-secret mounts or Docker socket, runs as UID/GID 65534 with read-only filesystem, dropped capabilities, no-new-privileges, 96 MiB and 0.10 CPU limits, and disables topic auto-creation. This internal monitoring path does **not** verify the Container Apps' private SSL 9092 path or its CA/hostname verification; those need separate existing Kafka checks.

Prometheus exposes port 9090 on VM loopback only. GitHub/Jira `/metrics` targets are private Container App FQDNs over HTTPS, with stable `instance` relabeling and semantic service/environment labels. Both services call `MapMetrics()` in their existing application pipeline. The Azure template renders `insecure_skip_verify` from existing `ACA_TLS_VERIFY`: the default is `on`, but an existing `off` override is supported. Live verification must inspect the **rendered** TLS setting and identify any override; local synthetic validation using `false` does not prove production TLS verification. This task changes neither the override nor ingress.

The outbox sampler has a five-second cancellation budget and runs once per minute. Scrapes read cached gauges, never query MySQL. A failed sample preserves the last depth but marks it invalid; freshness gates prevent reporting that value as healthy. Held gauges reset on process startup. Counters are observations, not a durable audit ledger: crashes around transaction/counter boundaries can undercount, and restarts reset counters. `increase`/`rate` should be used for incident windows.

The custom Kafka metric implementation permits at most **52 samples per service registry** once both histograms are instantiated: three publish counters, seven consume counters, two retry counters, two histograms of 17 samples each (14 finite buckets, +Inf, sum, count), four single-valued gauges and two outbox status gauges. This excludes scrape labels, replicas, recording rules, exporter metrics and other application metric families. Those dimensions multiply actual stored series. Topic/group labels are restricted to the existing registry, not arbitrary event identities. This follows the [Prometheus guidance on avoiding high-cardinality labels](https://prometheus.io/docs/practices/naming/).

No Alertmanager/email/Slack/paging route is configured by this implementation. Alerts are Prometheus rules visible through Prometheus and the Grafana dashboard's `ALERTS` panels; the task does not establish external notification delivery or Grafana-managed alert rules.

## All ten Kafka alerts

Every name below has prefix `ResearchTrackKafka`. Application, group-lag and outbox conditions require the corresponding service's Kafka-enabled metric. Infrastructure monitoring remains active when applications are disabled. The legacy Test exporter target is explicitly optional; production is not.

| Suffix | Firing condition | `for` / severity | Recovery and local proof |
| --- | --- | --- | --- |
| ExporterUnavailable | Required exporter UP=0 or whole exporter job absent | 2m / critical | Required scrape returns; existing exporter recovery scenario. Real fixture shows exporter-unavailable firing during broker outage and clearing after recovery. Missing scrape leaves broker root cause unknown. |
| BrokerUnavailable | Recorded broker health=0 while exporter UP=1 | 2m / critical | Count and live smoke offset become valid; existing broker recovery scenario. Real outage assertion accepts either exporter or broker root cause; it does not claim both fired. |
| PublishingFailures | >=3 retryable/permanent publication failures in 10m | 2m / warning | Failures age out; existing counter-reset/window recovery case. One real producer timeout is correctly below the alert threshold. |
| EventProcessingFailures | >=3 failed/rejected handoff attempts in 10m | 2m / warning | Window ages out; existing recovery case. Real fixture proves firing after three processing failures; final alert may still fire until the window expires. |
| OffsetCommitFailures | >=3 commit failures in 10m | 2m / warning | Window ages out; **new** Jira recovery case. A single actual commit failure verifies the metric/replay, not this alert's firing threshold. |
| ConsumerHeld | Enabled consumer held=1 | 1m / critical | Reviewed recovery resets held state; existing recovery case and real fixture firing/clearing. |
| ConsumerLagHigh | Sum of approved measured group lag >100 with known offsets | 10m / warning | Measured lag drains <=100; existing recovery case and real >=128→0 scenario. Unknown/missing groups do not become zero. |
| OutboxBacklog | Fresh, successful pending depth >=100 | 5m / warning | Fresh count drains below100; existing backlog recovery case. Real outage retains one row, intentionally below alert threshold. |
| OutboxFailed | Fresh, successful failed depth >0 | 2m / warning | Fresh count returns to zero after separately approved recovery; **new** synthetic case. No real failed row is cleared to demonstrate it. |
| OutboxMetricsUnavailable | Enabled sampler failed, missing or age >=180s | 5m / warning | Successful fresh sample returns; **new** synthetic sampler recovery and freshness expression assertion. Startup snapshots can temporarily show sampler unavailability in accelerated fixture evaluation. |

The new tests also assert non-firing before each production `for` duration. Production alert-state semantics are described in [Prometheus alerting rules](https://prometheus.io/docs/prometheus/latest/configuration/alerting_rules/).

## Executed tests and retained evidence

All commands ran locally on macOS arm64 with Docker Desktop, .NET SDK 10.0.400 and Python 3. The validator uses `prom/prometheus:v3.15.0` promtool with network disabled, read-only inputs/root and bounded `/tmp` scratch space; it creates synthetic required Compose env files, never reads production Grafana/MySQL env files, and normalizes only disposable input permissions for the non-root image.

| Command/check | Actual result | Evidence |
| --- | --- | --- |
| `./scripts/test-kafka-observability.sh` before changes | **PASS** —159 checks,30 scenarios,55 expressions | `artifacts/kafka-observability-task08/observability.log` |
| Same command after three added recovery cases | **PASS** —159 checks,33 scenarios,55 expressions | `observability-final.log` in the same directory |
| `dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj -c Release --no-build --filter FullyQualifiedName~KafkaMetricsTests` | **PASS** —9 executed,0 failed,0 skipped | `kafka-metrics.log`, `kafka-metrics.trx` |
| `python3 deploy/azure/validation/test_monitoring_reconciliation.py` | **PASS** —12 tests | `monitoring-reconciliation.log`; mocked execution of actual reconciliation script, including validation before recreation and unchanged broker/database/edge identities. |
| `./scripts/test-kafka-integration.sh` | **PASS** —GitHub3/3 (3m52s), Jira2/2 (2m48s), zero skips; runner exit0 | `isolated-kafka.log`; TRX and snapshots under the project directory below. |
| Release solution build inside isolated runner | **PASS** —0 warnings,0 errors | Same integration log; 2.77s build. |
| Shell syntax and diff whitespace | **PASS** —`bash -n` for both test runners, monitoring reconciliation and VM validation; `git diff --check` | No workflow or production script edited. |
| Exact report PromQL syntax | **PASS** —21 operator expressions parsed using the same isolated promtool image | `artifacts/kafka-observability-task08/report-promql.log` |
| Production read-only inspection/Grafana browser panels | **NOT EXECUTED** | Separate authorization and target access required. |

Fixture project: `rt-kafka-fixture-129c61802748`.
Evidence: `artifacts/kafka-integration/rt-kafka-fixture-129c61802748/`.
Retained data: `/private/var/folders/sm/87_wdrf12p1dh_1zt1djm7qr0000gn/T/researchtrack-kafka-fixture.sNUfXd`.
The runner stopped only its five disposable containers; data and volumes are retained. These ignored artifacts are local evidence, not committed documentation or production artifacts.

Observed evidence:

- GitHub and Jira outage snapshots contain a firing `ResearchTrackKafkaExporterUnavailable`; recovered snapshots contain neither broker nor exporter unavailability. Actual pending outbox survives timeout, retries once and becomes PUBLISHED.
- Per-service commit-failed and consumer-diagnostic snapshots capture one commit failure followed by successful replay/commit, without permanently held state.
- GitHub held recovery uses a fresh registry: 128 duplicate handoffs, 128 successful commits, zero new durable successes; one receipt and one business sync remain. Counter resets are expected, not missing event processing.
- Final `healthy-*.json` has broker health 1 and actual UP exporter, broker count 1 and nonnegative existing smoke offset; it does not assert every Kafka alert is absent.
- Final retained offsets: GitHub 130/130 and Jira 2/2, both lag 0. Groups have no active members after test consumers close; this is expected and does not contradict measured zero lag.
- GitHub business proof: one commit, one sync run, one receipt. Jira business proof: issue RT-42, syncRevision 1, one sync job, one receipt. Provider responses are deterministic test stubs.
- Fixture application metrics are real worker/handler registries exported through a **test-only snapshot server**, not the deployed ASP.NET `/metrics` endpoint. Grafana is not started in this isolated suite.

The isolated runner shortens alert `for` durations to 10s and rule-group interval from 30s to 5s; expressions/thresholds are retained. Production durations are independently tested unchanged by promtool. Therefore the fixture's elapsed firing time is not a production response-time guarantee.

### Reproduce on macOS

From this worktree with Docker Desktop running and .NET10/Python3/OpenSSL available:

```bash
./scripts/test-kafka-observability.sh
env -u RT_KAFKA_TEST_ENABLE dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj -c Release --filter FullyQualifiedName~KafkaMetricsTests
python3 deploy/azure/validation/test_monitoring_reconciliation.py
./scripts/test-kafka-integration.sh
```

Expect 159 checks, 33 scenarios, 55 expressions; nine metric tests; 12 reconciliation tests; GitHub 3 and Jira 2 isolated tests **executed with zero skips**. The runner requires free loopback ports 19092/13307/19090 and a local Unix Docker endpoint; do not stop unrelated containers to free them. Missing dependencies/daemon yield NOT EXECUTED, not PASS. Keep artifacts from failures as well as successes. Do not use global Docker prune, delete retained storage or substitute Azure endpoints.

## Separately approved read-only deployed verification

No live command below was executed in TASK08. Request authorization covering the existing production VM, Prometheus/Grafana read access, intended release SHA and evidence window. Use the established operator access and existing Grafana account; no new monitoring permissions, credentials, routes, ports or resource changes are needed.

### 1. Record deployed versions/configuration

On the **approved existing VM**, use only inspection operations:

```bash
date -u
for service in kafka kafka-exporter prometheus grafana; do
  container_id="$(/opt/researchtrack/scripts/compose.sh ps -q "$service")"
  test -n "$container_id" || exit 1
  docker inspect --format '{{.Name}} image={{.Config.Image}} id={{.Image}} status={{.State.Status}} started={{.State.StartedAt}} ports={{json .NetworkSettings.Ports}}' "$container_id"
done
/opt/researchtrack/scripts/compose.sh exec -T prometheus promtool check config /etc/prometheus/prometheus.yml
sha256sum /opt/researchtrack/prometheus/prometheus.yml /opt/researchtrack/prometheus/rules/researchtrack-alerts.yml /opt/researchtrack/grafana/provisioning/dashboards/json/kafka-observability.json
awk '/insecure_skip_verify:/ {print}' /opt/researchtrack/prometheus/prometheus.yml
```

Expect broker/exporter running; exact exporter v1.10.0 digest; no exporter host binding; Prometheus loopback only; valid loaded configuration; private target/TLS setting matches the approved environment. Capture service revision/image SHAs through the separately authorized existing read-only Container Apps procedure. Config hashes need comparison to the **approved deployed release**, accounting for rendered placeholders; uncommitted worktree hashes are not a deployed baseline.

Do **not** run `validate-vm-stack.sh` in its default mode just for metrics: it can publish a Kafka smoke record. Its existing monitoring checks are useful implementation evidence, but this acceptance uses read-only fragments. Do not run reconciliation, reload, restart, `--smoke`, topic mutation or offset reset.

### 2. Check targets and loaded rule health

Use VM loopback or the established authorized tunnel. Do not expose 9090 publicly. These HTTP GET endpoints and instant queries are documented in the [Prometheus HTTP API](https://prometheus.io/docs/prometheus/latest/querying/api/).

```bash
curl --fail --silent --show-error --connect-timeout 3 --max-time 10 http://127.0.0.1:9090/-/ready
curl --fail --silent --show-error --connect-timeout 3 --max-time 10 http://127.0.0.1:9090/api/v1/targets \
  | jq -e '.status == "success" and ([.data.activeTargets[] | select(.labels.job == "researchtrack-kafka")] | length) == 1 and all(.data.activeTargets[] | select(.labels.job == "researchtrack-kafka"); .health == "up" and .labels.service == "kafka" and .labels.environment == "production")'
curl --fail --silent --show-error --connect-timeout 3 --max-time 10 http://127.0.0.1:9090/api/v1/targets \
  | jq '[.data.activeTargets[] | select(.labels.job == "researchtrack-services" and (.labels.service == "github" or .labels.service == "jira")) | {service:.labels.service, environment:.labels.environment, instance:.labels.instance, health,lastScrape,lastScrapeDuration}]'
curl --fail --silent --show-error --connect-timeout 3 --max-time 10 http://127.0.0.1:9090/api/v1/rules \
  | jq '.data.groups[] | select(.name == "researchtrack-kafka") | {name,interval,rules:[.rules[] | {name,type,health,state,duration,lastEvaluation}]}'
```

Expect one exporter and two service targets with the correct labels, all UP and fresh lastScrape. The rules group must contain one health recording rule and **all ten named alerts**, with rule health `ok` and current evaluations; mere presence is not sufficient. Preserve pending states too. Do not attach full target/configuration dumps if they contain private addresses or authentication details; retain selected redacted fields.

### 3. Exact read-only PromQL and expected results

Paste the expressions below into the existing Grafana datasource's Explore view, or call the VM-local query API:

```bash
rt_prom_query() {
  curl --fail --silent --show-error --get --connect-timeout 3 --max-time 10 \
    --data-urlencode "query=$1" --data-urlencode 'timeout=5s' \
    http://127.0.0.1:9090/api/v1/query \
    | jq -e 'if .status == "success" then {data,warnings,infos} else error("Prometheus query failed") end'
}
rt_prom_query 'researchtrack_kafka_enabled{job="researchtrack-services",environment="production",service=~"github|jira"}'
```

An empty result is unknown/missing evidence, not zero. Query success alone is not acceptance. Observe two fresh scrapes where appropriate; traffic-counter rates can be zero when idle.

**A. Required target and metric presence** —expect exporter 1; GitHub/Jira each 1 for UP. Each service must have an enabled series, value 0 or 1 matching approved runtime config. An older image missing this series fails instrumentation verification even if scrape UP.

```promql
up{job=~"researchtrack-kafka|researchtrack-services",environment="production",service=~"kafka|github|jira"}
```

```promql
researchtrack_kafka_enabled{job="researchtrack-services",environment="production",service=~"github|jira"}
```

**B. Broker evidence** —expect health 1, count 1 and smoke partition 0 offset >=0. Count and HTTP UP alone are insufficient. Offsets can legitimately stay constant when idle.

```promql
researchtrack_kafka_broker_healthy{job="researchtrack-kafka",environment="production"}
```

```promql
kafka_brokers{job="researchtrack-kafka",environment="production"}
```

```promql
kafka_topic_partition_current_offset{job="researchtrack-kafka",environment="production",topic="researchtrack.deployment-smoke",partition="0"}
```

**C. Approved topic state** —expect the existing smoke/GitHub/Jira topic rows with approved partition counts (currently 1 each). Check topic contracts if application topics are intentionally not yet provisioned; do not create them for monitoring.

```promql
kafka_topic_partitions{job="researchtrack-kafka",environment="production",topic=~"researchtrack\\.(deployment-smoke|github\\.events\\.v1|jira\\.events\\.v1)"}
```

**D. Producer/process/commit failures** —healthy post-incident windows normally 0. Positive values remain evidence of recent failures even after repair. Current alerts require >=3 and stabilization; one error need not fire.

```promql
sum by (service,outcome) (increase(researchtrack_kafka_publish_total{job="researchtrack-services",environment="production",outcome=~"retryable_failure|permanent_failure"}[10m]))
```

```promql
sum by (service,stage,outcome) (increase(researchtrack_kafka_consume_total{job="researchtrack-services",environment="production",outcome=~"failed|rejected"}[10m]))
```

**E. Acknowledged publication and durable handoff/commit** —zero valid while idle; legitimate separately approved provider traffic should increase publish success, process success/duplicate/noop as appropriate, and commit success. Do not demand identical totals across replicas/restarts or use this as proof of business completion.

```promql
sum by (service) (increase(researchtrack_kafka_publish_total{job="researchtrack-services",environment="production",outcome="success"}[10m]))
```

```promql
sum by (service,stage,outcome) (increase(researchtrack_kafka_consume_total{job="researchtrack-services",environment="production",outcome=~"success|duplicate|noop"}[10m]))
```

**F. Retries and held state** —retry 0 valid when no failures; retries rising during natural recovery are separate from successes. Held should be 0 in a healthy enabled consumer. A disabled service's initialized zero does not prove an active consumer.

```promql
sum by (service,stage) (increase(researchtrack_kafka_retry_total{job="researchtrack-services",environment="production"}[10m]))
```

```promql
researchtrack_kafka_consumer_held{job="researchtrack-services",environment="production"} and on(service,environment) researchtrack_kafka_enabled{job="researchtrack-services",environment="production"} == 1
```

**G. Group presence, offsets and measured lag** —one member is expected for each running single-replica consumer, but confirm deployed replica/group settings. Zero members means an inactive measured group; absent group/negative offset is unknown. Existing committed groups should drain to 0 lag under normal completed traffic.

```promql
kafka_consumergroup_members{job="researchtrack-kafka",environment="production",consumergroup=~"researchtrack-(github|jira)-webhook-v1"}
```

```promql
kafka_consumergroup_current_offset{job="researchtrack-kafka",environment="production",consumergroup=~"researchtrack-(github|jira)-webhook-v1",topic=~"researchtrack\\.(github|jira)\\.events\\.v1"}
```

```promql
sum by (consumergroup,environment) (
  kafka_consumergroup_lag{job="researchtrack-kafka",environment="production",consumergroup=~"researchtrack-(github|jira)-webhook-v1",topic=~"researchtrack\\.(github|jira)\\.events\\.v1"}
  and on(job,instance,environment,consumergroup,topic,partition)
  kafka_consumergroup_current_offset{job="researchtrack-kafka",environment="production"} >= 0
)
```

**H. Sampling and trustworthy outbox depths** —enabled services should sample successfully, age normally <90s and always <180s for trusted depth. Pending 0 after drain; failed 0 unless retained work needs review. Missing gated depths must be investigated, not rendered as healthy zero.

```promql
researchtrack_kafka_outbox_sample_success{job="researchtrack-services",environment="production"} and on(service,environment) researchtrack_kafka_enabled{job="researchtrack-services",environment="production"} == 1
```

```promql
(time() - researchtrack_kafka_outbox_sample_timestamp_seconds{job="researchtrack-services",environment="production"}) and on(service,environment) researchtrack_kafka_enabled{job="researchtrack-services",environment="production"} == 1
```

```promql
researchtrack_kafka_outbox_messages{job="researchtrack-services",environment="production",status=~"pending|failed"}
and on(service,environment) researchtrack_kafka_outbox_sample_success{job="researchtrack-services",environment="production"} == 1
and on(service,environment) (time() - researchtrack_kafka_outbox_sample_timestamp_seconds{job="researchtrack-services",environment="production"}) < 180
and on(service,environment) researchtrack_kafka_enabled{job="researchtrack-services",environment="production"} == 1
```

**I. Evaluated alerts** —healthy established targets normally yield no pending/firing Kafka rows. Check loaded rule health/metric presence first; an empty alert vector by itself cannot prove coverage. Entire missing-job alerts may lack an environment label, so also inspect the unfiltered category query.

```promql
ALERTS{category="kafka",environment="production",alertstate=~"pending|firing"}
```

```promql
ALERTS{category="kafka",alertstate=~"pending|firing"}
```

**J. Kafka metric series inventory** —inspect family/count and sampled label **names**, redact identities in unrelated families. Compare per-target count with configured replicas and the 52/service bound above. It is not a whole-service privacy certificate.

```promql
count by (__name__,service,instance) ({__name__=~"researchtrack_kafka_.+",job="researchtrack-services",environment="production"})
```

### 4. Grafana acceptance

Use the existing HTTPS Grafana hostname with normal certificate verification and existing operator login. Do not add tokens to query text, URLs or screenshots. In the ResearchTrack folder, inspect:

- `researchtrack-overview`, `github-sync-operations`, `jira-integration-operations` and `researchtrack-kafka-observability` UIDs.
- Datasource UID `prometheus`, proxy URL `http://prometheus:9090`, connection success through the existing UI.
- Kafka dashboard's 18 operational panels with Environment=`production`; original overview/service dashboards still render. Retry and commit-failure queries are available in Explore even though there is no dedicated retry/commit panel.
- Browser time range includes fresh scrapes; no query errors; missing groups remain no-data; idle publication rates 0 are allowed; P95 latency with no observations may be NaN/no-data.
- Active alert tables reflect actual Prometheus `ALERTS`, including the intentionally environment-unfiltered infrastructure alert table. They are not external notification proof.

Retain timestamp, approved release/ref, selected image IDs, redacted target/rule results, exact query/result pairs and screenshots with visible environment/time range. Do not export secrets, complete environment files, raw provider payloads or unrestricted metric/log dumps. No production failure simulation is needed: use the deterministic isolated evidence for firing/recovery and observe natural incidents read-only if they occur.

## Changes, remaining evidence and completion checklist

TASK08 changes exactly:

1. `deploy/monitoring/tests/kafka-alerts.test.yml`: three uncovered recovery scenarios, including pre-`for` negative checks, separation from process failures and restored sampler freshness. Synthetic failed-row disappearance is test input, not a new production cleanup policy.
2. `docs/devops/KAFKA_OBSERVABILITY_FINAL_REPORT.md`: this verification report and operator procedure.

No confirmed runtime/configuration defect requiring a production-rule or application-code change was found. Existing TLS, Kafka contract, offsets, retries, migration, runtime restoration and rollout behavior are preserved.

Remaining evidence: deployed Prometheus/Grafana image compatibility (production Compose still uses existing `latest` tags; local promtool is pinned), rendered TLS setting, exact running GitHub/Jira images and metric presence, fresh private scrape/label state, browser rendering and live rule health. Consider pinning monitoring images later through an approved release; do not infer compatibility from syntax alone. External notifications are outside the current configured scope. No throughput/cardinality-at-scale load test was performed.

- [x] Configured broker/exporter/private scrape controls inspected.
- [x] Producer/process/commit failures, retries, held state and outbox metrics tested.
- [x] All ten alert conditions and recovery cases locally tested.
- [x] All provisioned dashboard expressions validated; legacy definitions preserved by existing hash checks.
- [x] Real disposable broker failure/recovery, lag drain and deduplication passed.
- [x] Label privacy/cardinality implementation and existing regression checks reviewed.
- [x] Exact read-only production queries and expected outcomes prepared.
- [ ] Deployed image/configuration/metric presence and rendered TLS evidence retained.
- [ ] Production Grafana rendering and live rule-state evidence retained after approval.
- [ ] Story live-acceptance sign-off by the operator; local PASS does not close these missing items.

Rollback for this local change is removal of the three new synthetic scenarios and this report only. Production restoration follows the separately authorized existing [Kafka observability runbook](azure-production/KAFKA_OBSERVABILITY.md); it is not executed here. No commit, push, workflow dispatch, deployment or production monitoring change occurred.
