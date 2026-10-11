# DEVOPS-4.4 — Kafka failure recovery validation

Date: 2026-10-11. Worktree: `researchtrack-backend-integration-main-kafka`.
Branch: `integration/main-kafka-production-20261011`.
Scope: local test infrastructure, regression coverage, runbook and proposed verifier correction.
No Azure resource, production broker, topic, database, volume, secret or workflow mutation
was performed. No commit, push, merge, deployment or workflow dispatch was performed by
this task. The original feature-branch checkout was not edited.

## Existing implementation reused

The approved GitHub/Jira v1 topics carry immutable webhook references. Authentic ingress
persists webhook and outbox atomically. Production publication uses verified TLS, idempotence,
acks-all, bounded buffering and a 30-second delivery timeout. MySQL claims outbox leases
atomically and preserves per-key ordering. Retry delays are 30 seconds, 2/10/30 minutes,
then 1 hour; after ten attempts, FAILED work stays durable and can block its key.

Handlers create durable deduplication receipts and existing business work within transactions.
Consumers disable automatic commits/stores; offsets commit after durable handoff. A commit
failure closes/rejoins the group and deduplicates replay. Invalid records or exhausted
processing hold the consumer without skipping offsets. Existing GitHub delivery/sync workers
and Jira scheduling/sync workers perform the actual business processing. The VM broker is
single-node KRaft/RF1 on persistent binds, so outages temporarily remove broker availability.
Existing Prometheus/Grafana metrics, alerts and dashboards are preserved.

## Exact task file list

| File | Change |
|---|---|
| `scripts/test-kafka-integration.sh` | Local Docker/port preflight, guarded fixture identity, retained data, per-run evidence, required MySQL TLS, no-skips TRX assertion and final smoke |
| `tests/Kafka/prometheus.yml` | Match production broker scrape interval/timeout, avoiding overlapping cached scrapes |
| `tests/Kafka/compose.yml` | Explicit isolated broker label, correct snapshot HTTP server, fixture exporter CPU budget |
| `tests/Kafka/IsolatedKafkaFixture.cs` | Controlled outages, persisted retry/identity/topology/offset checks, automatic real consumer worker recovery, HTTP/metrics/alert/business evidence helpers |
| `tests/Kafka/broker-control.sh` (new) | Fail-closed stop/start/snapshot of only the runner's exact disposable broker |
| `tests/Kafka/test_broker_control.py` (new) | Seven adversarial broker-boundary tests and one actual HTTP media-type regression |
| `tests/Kafka/serve_metrics.py` (new) | Correct Prometheus text media type for real registry snapshots |
| `tests/Kafka/verify_observability.py` | Final scrape/alert, both group lag and recorded business-result checks |
| `tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs` | Extends existing real GitHub flow with outages, automatic rejoin, dedup and business evidence |
| `src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs` | Exact persisted timestamp validation in SQL avoids Connector/NET fractional-second loss; no schema/contract change |
| `tests/ResearchTrack.JiraService.Tests/KafkaMessagingTests.cs` | New regression rejects a one-microsecond identity mismatch without a receipt/job |
| `tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs` | Extends existing real Jira flow with the same recovery and actual issue/revision evidence |
| `tests/ResearchTrack.Testing/ResearchTrackWebApplicationFactory.cs` | Deterministic disabled Kafka default prevents accidental inherited broker connections |
| `deploy/azure/validation/test-kafka-operations.sh` | Seven mount regression cases and fixture safety tests through the existing CI invocation |
| `deploy/azure/vm/scripts/diagnose-kafka-mounts.sh` (new) | Read-only selected mount/image/filesystem diagnostic |
| `deploy/azure/vm/scripts/verify-kafka.sh` | Proposed minimal mount assertion correction; preserves all exact required bind/security checks |
| `docs/devops/azure-production/KAFKA_INTEGRATION_TESTING.md` | Current macOS reproduction steps, retained artifacts and smoke acceptance |
| `docs/devops/azure-production/KAFKA_OPERATIONS.md` | Recovery diagnosis, mount hypothesis, expected outputs, rollback and evidence requirements |
| `docs/devops/azure-production/DEVOPS_4_4_REPORT.md` (new) | This review report |

Pre-existing workflow/branch-policy/document changes were preserved. During the session an
external commit, `464551d`, incorporated those pre-existing changes and an earlier version
of some task work. This task did not create that commit. The remaining edits are unstaged
locally; review both that external commit and the current worktree diff to see the complete
result. The baseline was `bdb8173`; baseline diffs are retained under `artifacts/devops-4.4/`.

## Added/extended tests and their proof

1. **Existing GitHub and Jira live flows extended**, rather than separate duplicate flows:
   two broker outages per service; actual MySQL webhook/outbox and actual TLS clients.
   A failed publication retains the original PENDING row/attempt/error and future retry;
   an immediate poll makes no second attempt. No receipt exists before publication.
   The same producer retries the naturally eligible row after restart and acknowledges
   PUBLISHED/attempt 2. Broker metadata, topics/partitions and committed offsets persist.
2. **Failure between handoff and commit**: a test-only handler delegates the real handler,
   then stops only the disposable broker before commit. The production consumer worker
   records commit failure and automatically closes/rejoins. Its second handler invocation
   deduplicates; the broker's exact next offset is committed. No test reset/seek/table
   repair/app restart is used for this transient failure.
3. **Actual business outcomes**: GitHub's existing worker/sync produces one mirror head SHA,
   commit and sync run; Jira's existing worker/sync produces the expected issue and revision.
   Replay leaves one durable receipt and one logical result/job. Provider API responses are
   synthetic doubles; provider network/API compatibility is not asserted.
4. **Availability and monitoring**: real application HTTP live/readiness/Swagger routes stay
   HTTP 200; messaging health changes Healthy → Degraded → Healthy. Actual application
   registry snapshots are scraped by real Prometheus; publication failures/retries and
   backlog are visible; broker/exporter unavailability fires then resolves. The existing
   GitHub held-consumer/128-replay test proves lag/held/processing failure alerts and durable
   deduplication. Unrelated MySQL/Prometheus/snapshot containers retain ID/start time.
5. **Seven broker-control tests** reject production endpoint/project, mismatched container,
   missing label, wrong host mount/public port and unsupported operations before mutation;
   valid start/stop is scoped to `kafka` only. The eighth test starts an actual local HTTP
   server and verifies original metric body plus the supported Prometheus content type.
6. **Seven mount cases**: accept known anonymous image volumes; reject named image volumes,
   extra host bind, nested data shadow mount, duplicate destination, writable configuration
   and missing required bind. Existing TLS, identity, topic and operational tests remain.

7. **Exact Jira timestamp regression** alters the persisted timestamp by one microsecond
   while retaining the owned immutable outbox payload. Consumption must reject it and
   create no receipt/job. The live flow also reads the stored timestamp through DateTime
   to prove microseconds were stored correctly before consuming the Kafka event.

Existing unit tests already prove ten-attempt exhaustion/FAILED retention, ordering,
lease recovery, transactional rollback, contract rejection, disabled compatibility and
manual-commit policy; these were rerun, not duplicated.

## Executed results

| Check | Actual result / evidence |
|---|---|
| Isolated Kafka/MySQL end-to-end recovery suite | PASS: GitHub 2/2, Jira 1/1, zero failures/skips; final runner exit 0, `kafka-integration-attempt8.log` |
| .NET Release solution build | PASS, zero warnings/errors; runner/build logs |
| Relevant solution regression suite | PASS: 823 executed, zero failed/skipped; `regression-verified/*.trx` |
| Kafka-related unit/flow/metrics/configuration regressions | PASS: 53 within those 823 (GitHub 42, Jira 11) |
| Kafka operational safety | PASS: 50 shell cases + 8 Python cases; `kafka-operations-final.log` |
| Kafka observability/rules/dashboard validation | PASS: 158 config checks, 30 promtool alert scenarios, 55 parsed dashboard queries; `observability-final.log` |
| Kafka topic/renderer/CA configuration | PASS: 21 tests; `kafka-config.log` |
| Monitoring reconciliation | PASS: 11 tests; `monitoring-reconciliation.log` |
| Service environment composition / Azure environment validation | PASS: 10 / 22 cases |
| Kafka smoke / network probe / runtime readiness / runtime power checks | PASS: 9 / 12 / 4 / 11 offline cases, logs retained |
| Image selection / production preflight / private-IP regressions | PASS: existing stubbed offline suites; no cloud calls |
| Bash syntax | PASS: 68 scripts |
| ShellCheck default severity | FAIL: informational source-following/JQ-literal/conditional findings retained in raw log |
| ShellCheck warning/error severity | PASS: five affected shell scripts; no warning/error findings |
| actionlint 1.7.12, unfiltered | FAIL: ten existing findings (five unsupported `concurrency.queue`, five SC2129 style findings) |
| actionlint with only those baseline findings excluded | PASS; all workflows checked, no workflow edited |
| Production broker failure injection / deployment / live mount inspection | NOT EXECUTED; prohibited by task scope |
| MySQL migration upgrade / other `DatabaseIntegration` category | NOT EXECUTED; fixture uses EnsureCreated; no production database access |

The first seven full isolated attempts failed and their logs are retained: (1) MySQL caching-SHA2
rejected the previous unencrypted fixture connection, corrected to required TLS; (2) Prometheus
3 rejected static server's octet-stream media type, corrected to Prometheus text format;
(3) offset query timed out during coordinator reconnect, now bounded polling of the same
consumer; (4) intermittent exporter scrape/alert state under a 0.1 CPU fixture cap, increased
fixture cap and shortened fixture evaluation only; (5) the previous fixture's 5-second
scrapes still alternated cached success/timeouts, corrected by matching production's
30-second broker scrape interval and 10-second timeout; (6) first outage/retry/alert recovery
passed, then a fresh read-only diagnostic consumer returned Local_DestroyBroker during
bootstrap, now retried within a strict 60-second bound on the same client. Alert expressions
and production client configurations remain unchanged; (7) GitHub 2/2 passed, Jira
publication recovered but its consumer held before commit. A focused diagnostic proved
Connector/NET returned seconds-only DateTimeOffset while MySQL retained microseconds.
The handler now compares the exact persisted timestamp in SQL, retaining strict identity
validation. The diagnostic runner is explicitly partial and cannot satisfy the full
three-test final smoke on its own. The focused Jira test passed after the correction
(1/1 executed, zero skips); its partial runner deliberately cannot pass the missing
GitHub TRX requirement. The complete final run is recorded separately. Do not treat timeout-test PASS or a
partial TRX from a crashed test process as full suite success. The runner now requires all
three expected tests executed/passed with no skips.

Prometheus' format requirement is documented in the
[official exposition format](https://prometheus.io/docs/instrumenting/exposition_formats/).
Production alert expressions, thresholds and timings are unchanged. The disposable fixture
uses 10-second `for` and 5-second evaluation timing to keep failure injection reproducible.

## Jira precision defect found by the live suite

The database retained `2026-10-11 10:25:23.058022`, while the provider materialized
DateTimeOffset as `2026-10-11T10:25:23.000000+00:00`. The consumer's prior in-memory exact
comparison therefore rejected its own valid event. The
[Connector/NET reader source](https://raw.githubusercontent.com/mysql/mysql-connector-net/9.x/MySQL.Data/src/MySqlDataReader.cs)
converts this type through a string parse, matching the observed loss of fractional seconds.
The narrow fix performs exact event-id/timestamp equality in MySQL rather than rounding
identity timestamps. Project/cloud/type checks, immutable outbox ownership, transactions,
receipts and offsets remain intact. No model, migration, table, topic or TLS setting changed.
Existing retained valid outboxes can be processed after a separately approved application
rollout/restart of an already-held consumer; never reset offsets or edit timestamps in data.

## Final isolated evidence

Final project: `rt-kafka-fixture-f58c8c700f16`; artifacts:
`artifacts/kafka-integration/rt-kafka-fixture-f58c8c700f16/`.
GitHub test duration 4m10s, Jira 2m51s; runner exit 0. All containers were stopped by
project-scoped cleanup; Kafka files, MySQL volume and other fixture data were retained.

- GitHub group: committed 130 / log end 130 / lag 0; one receipt, one sync run,
  one commit and expected mirror SHA after all replay checks.
- Jira group: committed 2 / log end 2 / lag 0; issue RT-42 / Synthetic Kafka issue,
  SyncRevision 1, one job and one receipt after both replay checks.
- Each original outbox: PENDING / attempt 1 → PUBLISHED / attempt 2; immutable event ID
  retained and scheduled retry honored without database edits.
- Per-service commit failure and exact recovered next offset are recorded in
  `*-commit-failed.evidence`, `*-offset-recovered.json` and final TRX.
- Broker health 1, exporter scrape healthy, broker/exporter firing alerts resolved;
  held/lag alerts fired and resolved. Historical processing failure alert can remain
  within its production expression's ten-minute window, as designed.
- Live/ready/Swagger HTTP 200; readiness Healthy → Degraded → Healthy.
- Topic partition count/RF1, KRaft metadata and unrelated fixture dependency identity
  checks passed. No topic recreation, offset reset, table repair or pending-event clear.

Independent final TRX/outbox/business evidence verification also passed; summaries are
`artifacts/devops-4.4/isolated-summary.json` and `regression-summary.json`.

## Mount diagnosis and narrow correction

The local official Kafka 3.9.1 image declares volumes `/etc/kafka/secrets`,
`/mnt/shared/config`, `/var/lib/kafka/data`. Production binds the last path but the first
two may be anonymous image volumes, making `.Mounts | length == 5` falsely fail. The local
proposal admits only those two additional anonymous volume destinations while requiring
all five exact binds, unique destinations and restrictive access. No live mount changed.
The new read-only diagnostic collects actual image/mount/filesystem evidence.

The original production assertion's actual mount dump was not available. A separately
inspected workflow failure (`38102308369`) was a GHCR TLS handshake while pulling the Meeting
image, not this assertion. Do not attribute that run to mounts. Confirm the original failure
with the read-only diagnostic before applying the proposed correction through a separately
approved release. No direct Azure CLI/Run Command or SSH diagnostic was performed here.

## Remaining risks / missing evidence

- Single-broker/RF1 has no availability failover; bounded retries retain work but cannot make
  Kafka available during an outage. Exhausted FAILED work and held consumers require review.
- Tests use real MySQL/Kafka/Prometheus but synthetic provider responses and in-process HTTP
  hosts. HTTP hosts suppress business schedulers to avoid duplicate processing and attach
  the actual flow's messaging health. They do not prove an ACA revision restart or load.
- EnsureCreated validates the current model, not migration application to an older database.
  Existing additive migration/unit assertions passed; no migration file changed.
- Registry snapshots prove real metric definitions and alert ingestion, not live Azure
  scrape endpoints, deployed images or Grafana UI delivery.
- Production synthetic end-to-end results, offsets, dashboards/alerts, actual mounts and
  readiness across a production recovery window remain NOT EXECUTED.
- Retained disposable data and stopped containers consume disk space; no deletion is part
  of this task. Temporary TLS/config/init files contain disposable secrets, keep them local.
- Review raw lint limitations and the external concurrent commit before publishing. Pushing
  the approved feature deployment branch can deploy; this task has not pushed anything.

## Safe reproduction and deployment acceptance

Follow the step-by-step [macOS Docker Desktop guide](KAFKA_INTEGRATION_TESTING.md#devops-44-reproducible-recovery-smoke-on-macos).
Review per-run TRX, topic/group summaries, HTTP/business/offset JSON, Prometheus evidence
and retained fixture identity before accepting a run. For deployment, follow the existing
protected release process only after separate authorization, first verifying additive
migration compatibility, topics/CA and revision rollback readiness. Generate only authorized
synthetic provider deliveries and capture the full durable handoff/business/offset chain.
Never delete/recreate topics, reset offsets, run migration Down or clear pending work.
Use the [recovery runbook](KAFKA_OPERATIONS.md#devops-44-recovery-diagnosis-and-final-smoke)
for read-only diagnostics and rollback decision criteria.

## Definition of Done

- [x] Existing broker/client/outbox/inbox/retry/offset/monitoring/ops architecture inspected.
- [x] Existing real GitHub/Jira flows extended with guarded disposable broker outages.
- [x] Durable retry, automatic commit recovery, dedup, business/HTTP/monitoring assertions added.
- [x] Final isolated recovery run executed with all three tests passed and zero skips.
- [x] Release build, 823 relevant regressions, observability and operational safety executed.
- [x] Shell/workflow checks executed; raw baseline findings explicitly reported.
- [x] Reproducible smoke, macOS instructions, diagnostics, rollback and evidence documented.
- [x] Mount assertion investigated and narrow local correction covered by regressions.
- [x] Production triggers/credentials, TLS, migrations and existing app behavior preserved.
- [x] No production mutation, commit, push, merge or deployment performed by this task.
- [ ] Actual production mount evidence and authorized live deployment/event/monitoring proof
      collected separately; not part of this local failure injection.
