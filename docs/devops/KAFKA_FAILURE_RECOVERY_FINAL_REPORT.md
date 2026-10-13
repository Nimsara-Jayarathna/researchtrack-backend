# TASK 09 — Kafka failure recovery and resilience verification

Story: **DO-404 / SCRUM-208**. Date: **2026-10-12**, Asia/Colombo.
Repository: `Nimsara-Jayarathna/researchtrack-backend`.
Branch: `integration/main-kafka-production-20261011`.
Base HEAD: `1810f09535d6a6d00947bcf84d18fde473e40c39`; existing TASK 02–08 uncommitted work retained.

## Result

**PASS: GitHub 3/3 and Jira 2/2 isolated Kafka tests, five executed and zero skipped.** The existing flows exercised real TLS Kafka 3.9.1, disposable MySQL 8.4, production Kafka workers and actual GitHub/Jira business processors with stubbed provider responses. Both groups finished at measured lag zero. The Release solution build passed with zero warnings/errors.

A missing direct persistence assertion was added to the existing GitHub flow: the complete 128-record acknowledged backlog must survive an ordinary broker stop/start, remain uncommitted while the consumer is held, and replay at contiguous offsets with exact keys/payloads. No sixth integration test, duplicate fixture, runtime-code change, offset reset or persisted-record repair was added.

These results establish the bounded isolated scenarios below. They do not establish production resilience, arbitrary crash/power-loss durability, provider behavior or exhaustive route availability. Any narrower untested scenario is explicitly **NOT VERIFIED**.

## Requirement-to-test matrix

Test aliases:

- **GH flow**: `GitHubKafkaFlowTests.TlsBrokerOutageDurableRetryCommitRecoveryAndActualBusinessOutcome`.
- **Jira flow**: `JiraKafkaFlowTests.AuthenticatedJiraWebhookBrokerOutageRetryCommitRecoveryAndActualSnapshot`.
- **Producer timeout**: `GitHubKafkaFlowTests.RealProducerTimeoutRetainsDurableWebhookAndOutbox`.
- **Outbox recovery**: shared `IsolatedKafkaFixture.RecoverOutboxAsync` called by both flows.
- **Commit recovery**: shared `RecoverConsumerCommitAsync` called by both flows.
- **Retained batch**: new `VerifyRetainedBatchRestartAsync`, followed by existing GH held/replay processing.
- **Messaging units**: existing GitHub/Jira `KafkaMessagingTests`, executed against disposable SQLite persistence; no production database.
- **Rule tests**: existing `scripts/test-kafka-observability.sh`, including TASK 08's recovery additions.

| # | Requirement | Test/assertion and evidence | Actual scope/result |
| --- | --- | --- | --- |
| 1 | Normal event processing | GH signed ingress and Jira authenticated webhook persist event/outbox; successful TLS publication, durable receipt and existing business worker update after recovery; normal duplicate publications also complete. `*-business.json`, `*-final.evidence`. | **VERIFIED** for successful resumed processing in these flows. Independent fresh no-outage Kafka business-flow scenario **NOT VERIFIED** in this suite. Provider calls are stubbed. |
| 2 | Broker outage | Guarded stop of only the identified disposable broker; native producer timeout and broker/exporter alert; no unpublished receipt. `*-outage.evidence`, `*-outage-alerts.json`. | **VERIFIED** for each service's controlled outage; no Azure endpoint used. |
| 3 | Durable pending messages | Original row retains EventId, PENDING, attempt 1, bounded error code and future retry; no clearing accepted work. `*-outbox-pending.json`. | **VERIFIED** with real MySQL for both services. |
| 4 | Outbox retry | Immediate second call returns false; actual production 30s backoff becomes eligible naturally. Same row eventually PUBLISHED, attempt 2, error cleared, retry metric 1. `*-outbox-published.json`, `*-recovered.evidence`. | **VERIFIED** for one scheduled retry. All later wall-clock delays during a long outage **NOT VERIFIED** end-to-end. |
| 5 | Broker recovery | Existing same broker container starts; KRaft metadata and topic/partition state retained; health1 and infrastructure alert gone. `identity-*.json`, recovery snapshots. | **VERIFIED** ordinary stop/start. No topic recreation or Kafka data repair. |
| 6 | Producer reconnection | Same `KafkaEventPublisher` retains its producer across outage and acknowledges naturally eligible retry. | **VERIFIED** in both flows; replacement publisher is not used to make this recovery pass. |
| 7 | Consumer reconnection | Existing consumer polls coordinator after recovery and sees unchanged committed offset; worker later reads recovered topic. | **VERIFIED** in both flows. |
| 8 | Offset commit | Real commit after durable handoff; fresh client sees exact next offset. Retained batch reaches130; final groups zero lag. `*-offset-recovered.json`, `group-*.txt`. | **VERIFIED**. Commit is after durable receipt/job handoff, not after remote business synchronization completion. |
| 9 | Offset commit failure | Wrapper stops disposable broker **after actual handler commits receipt**; native commit fails, distinct failed-commit metric 1. `*-commit-failed.evidence`. | **VERIFIED** for GH/Jira; no fabricated failed-commit exception in the real flow. |
| 10 | Consumer rejoin | Actual `KafkaConsumerWorker.ExecuteAsync` closes/rejoins after commit failure, obtains record again, deduplicates and commits; handlerCalls=2. | **VERIFIED**, automatically, without reset/seek or application restart. |
| 11 | Duplicate replay | Commit-failure replay and explicit duplicate publication retain one receipt. GH also replays128 retained duplicates. `*-final.evidence`, `github-retained-batch-processed.json`. | **VERIFIED**. |
| 12 | Idempotent business processing | GH one commit/one sync run; Jira one issue snapshot, one sync job, unchanged SyncRevision after replay; one receipt each. | **VERIFIED** for the actual stubbed-provider business paths. Concurrent multi-replica provider processing **NOT VERIFIED** here. |
| 13 | Consumer-held behavior | Existing actual consumer gets handler exceptions, exhausts bounded processing attempts and holds; lag/held/processing alerts fire. New offset assertion confirms committed offset remains 2 while held; fresh registry/consumer processes all 128 without duplicate business work. | **VERIFIED** for shared policy through GH. Jira-specific held-flow scenario **NOT VERIFIED**. This fresh registry models a reviewed restart; no full OS process restart is performed. |
| 14 | Retry exhaustion | `BrokerFailureRetainsInboxAndRetriesThenHoldsAfterBoundedAttempts` calls actual store claim/fail APIs with synthetic `now`, verifies ten attempts then FAILED/no further claim and Degraded health. Actual GH broken handler exhausts three processing attempts and holds. | **VERIFIED** for deterministic outbox policy and real consumer-worker bounded exhaustion. Ten actual producer timeouts across the full production backoff schedule on MySQL **NOT VERIFIED**; no database time patching was used to pretend otherwise. |
| 15 | Healthy→Degraded→Healthy readiness | Real ASP.NET pipeline and fixture MySQL, existing messaging health attached: GET readiness body has exact expected status before failure, after attempted timeout and after acknowledged retry. `/health/live`, `/health/ready`, Swagger remain200. | **VERIFIED** for both services under this fixture wiring. Degraded intentionally returns200. Held-consumer HTTP response mapping **NOT VERIFIED** by these flow checks. |
| 16 | Prometheus metric changes | Real registries expose failed publication1, pending1, retry1, success1; commit failure1 then success; duplicate counts; held/lag state. Snapshots scraped by real fixture Prometheus. | **VERIFIED** for tested changes. Test-only snapshot HTTP server is used; deployed ASP.NET metric scrape **NOT VERIFIED**. |
| 17 | Alert firing and recovery | Actual outage produces broker **or** exporter alert and clears on recovery; actual GH held/lag/processing failure alerts fire, held/lag clear after replay. Rule tests cover all ten firing/recovery conditions at production durations. | **VERIFIED** for these real transitions and deterministic rules. All ten real failure types **NOT VERIFIED** with actual outages. Processing-window alert can still fire at end; its eventual recovery is rule-tested, not waited out in real fixture. |
| 18 | Unrelated HTTP behavior | Actual GH/Jira GET liveness and Swagger stay200 throughout publisher outage; same MySQL/Prometheus/snapshot container IDs/start times remain. `*-http-*.json`, identity snapshots. | **VERIFIED** for these read-only routes/dependencies. Auth/Project/Meeting/Submission/Gateway and arbitrary business API routes during outage **NOT VERIFIED**. |
| 19 | Final business result | Existing business worker processes webhook to PROCESSED; GH mirror SHA/commit and Jira RT-42 summary/revision checked; original receipt/job counts retained. | **VERIFIED** against real disposable business database and deterministic provider responses. Real provider acceptance **NOT VERIFIED**. |
| 20 | No record loss across ordinary restart | New helper records low/high 0/130 and group offset 2 before stop/start; after recovery they remain 0/130 and 2. Existing group receives offsets 2..129, exact key/value/partition, commits 130; one receipt/sync remains. Two new JSON artifacts below. | **VERIFIED** for all 128 already acknowledged duplicate GH records and the single GH/Jira commit-failure replay records. Jira 128-record batch, distinct-event batch, forced kill/power loss/disk failure and retention-expired data **NOT VERIFIED**. |

## Smallest local extension

`tests/Kafka/IsolatedKafkaFixture.cs` adds a bounded retained-backlog restart helper:

1. Requires enabled fixture, exact `localhost:19092` endpoint and count 1..256.
2. Reads broker watermarks and group committed offset without subscribing, assigning, seeking or committing. Requires the complete batch immediately after the group offset.
3. Uses the existing guarded `broker-control.sh` stop/start; recovery runs in `finally` with a separate four-minute budget.
4. Verifies unchanged low/high watermarks, committed group offset and metadata hash, same broker container ID and unchanged MySQL/Prometheus/snapshot IDs/start times.
5. Waits for broker metric/alert recovery and writes selected non-secret evidence.

`tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs` calls this helper after the existing 128 acknowledged duplicate publications. It asserts no committed progress while held, verifies each consumed record's contiguous offset, partition, exact key and exact payload before durable handoff, and verifies committed next offset 130 after all 128. Final evidence records one receipt and one sync run.

This extends existing successful functionality without changing the integration count or shortening production retry/offset policies. No fault in production worker behavior was confirmed, so `KafkaMessagingWorkers.cs`, `KafkaPersistence.cs` and production settings are unchanged. Jira's existing exact SQL timestamp/MySQL microsecond correction and prior regressions remain intact.

## Executed validation

Evidence directory: `artifacts/kafka-recovery-task09/` (ignored, local).

| Validation | Actual result | Evidence |
| --- | --- | --- |
| `./scripts/test-kafka-integration.sh` | **PASS**, exit 0: GH 3/3 (4m20s), Jira 2/2 (2m57s), all five executed, zero skips | `isolated-kafka.log`; project TRX below. |
| Release solution build inside runner | **PASS**, zero warnings/errors; 6.82s | Integration log. |
| GH `KafkaMessagingTests` | **PASS**, 15/15, 0 failed/0 skipped; 7s | `github-messaging.log`, `.trx` |
| Jira `KafkaMessagingTests` | **PASS**, 15/15, 0 failed/0 skipped; 2s | `jira-messaging.log`, `.trx` |
| `python3 tests/Kafka/test_broker_control.py` | **PASS**, 8 tests | `broker-control.log`; adversarial fixture boundary tests and snapshot HTTP content type. |
| `./scripts/test-kafka-observability.sh` | **PASS**, 159 configuration checks, 33 synthetic scenarios, 55 dashboard expressions | `observability.log` |
| Shell syntax and diff whitespace | **PASS**, `bash -n` for both runners and broker control; `git diff --check` | Existing scripts remain unchanged. |
| Production chaos/live acceptance | **NOT EXECUTED / NOT VERIFIED** | No Azure access, broker operation or production event. |

Two initial ancillary command wrappers failed to create log files because they were started concurrently with evidence-directory creation; neither test started in those invocations. They were rerun after the directory existed and their executed results are recorded above. No skipped/error invocation is counted as a PASS.

### Current run artifacts

Project: **`rt-kafka-fixture-cfa1267c500d`**.
Artifacts: **`artifacts/kafka-integration/rt-kafka-fixture-cfa1267c500d/`**.
Retained data: `/private/var/folders/sm/87_wdrf12p1dh_1zt1djm7qr0000gn/T/researchtrack-kafka-fixture.TELVly`.

| Artifact | What it proves |
| --- | --- |
| `kafka-GitHub.trx`, `kafka-Jira.trx` | Actual executed tests/results; runner checks exact 3/2 counts and execution to reject skips. |
| `metrics/github-retained-restart.json` | 128 acknowledged records; firstOffset=2; before/after low=0, high=130; committedBefore/After=2; metadata, broker identity and unrelated dependency preservation. |
| `metrics/github-retained-batch-processed.json` | recordsVerified=128; contiguous offsets; exact keys/payloads; committedNextOffset=130; syncRuns=1, inboxReceipts=1. |
| `metrics/*-outbox-pending.json`, `*-outbox-published.json` | Same accepted identity before/after natural retry, attempt 1→2, PENDING→PUBLISHED; no failed-row repair. |
| `metrics/*-offset-recovered.json` | Actual next offset 1, handlerCalls=2; subsequent explicit duplicates advance GH/Jira offsets. |
| `metrics/*-commit-failed.evidence`, `*-consumer-diagnostic.evidence` | Actual commit-failure and automatic deduplicated recovery metrics. |
| `metrics/*-outage-alerts.json`, `*-recovered-alerts.json` | Infrastructure alert transition observed by actual fixture Prometheus. |
| `metrics/github-held-recovered.evidence`, `*-alerts.json` | Fresh registry duplicate=128, commit success=128, new durable success=0; held/lag clear. Processing failures may remain in the rolling window. |
| `metrics/*-http-degraded.json`, `*-http-healthy.json` | Read-only HTTP 200 and expected readiness bodies; healthy file contains the latest phase because the earlier healthy sample uses the same filename. TRX/assertions prove the full sequence. |
| `metrics/*-business.json` | Final business record/counts and event identity; provider responses are synthetic. |
| `metrics/identity-*.json` | Container IDs/start times and metadata hash around controlled operations. |
| `metrics/*-migration-upgrade.json` | Existing historical MySQL upgrade tests preserve seeded records/schema and additive receipt/outbox functionality. |
| `group-github.txt`, `group-jira.txt`, `topic-*.txt`, `healthy-*.json` | Final retained topics, offsets/lag, recovered exporter/broker and business snapshot checks. |
| `fixture-location.txt` | Exact disposable project/data location, retained by cleanup. |

Final group offsets are GH 130/130 and Jira 2/2, lag 0. Consumers have no active members after orderly test closure; this is expected. Snapshot/regression evidence is not production monitoring evidence. Temporary fixture credentials/TLS private keys are not review artifacts and must not be published.

## Recovery behavior and safety review

The real outbox worker publishes with acknowledgment before marking PUBLISHED; failures retain original work, enforce key ordering and schedule 30s/2m/10m/30m/1h retries. Claims use two-minute leases and a ten-attempt bound. FAILED rows remain held and block later work for the same key; persistent FAILED state survives a new health instance. No automatic exhausted-row replay policy is added.

Consumer processing uses up to three attempts, cooperative 15s cancellation per attempt and 1s/2s retry delays. Contract rejection and exhausted handler failure hold without committing. A broker offset-commit failure after durable handoff is different: the worker closes/rejoins, naturally replays from the broker's committed offset and uses the receipt hash to deduplicate. It does not read/commit a later record to bypass the failed commit. Downstream business completion remains separate from Kafka receipt and offset progress.

The new test never invokes Assign, Seek, group-offset alteration, topic deletion/recreation, volume removal, raw MySQL repair or pending-event clearing. Existing real flows wait for the actual production retry deadline. The deterministic bounded-exhaustion unit passes time through existing store APIs; it does not run ten actual native producer timeouts or modify real fixture retry timestamps to force a PASS.

Fixture control requires local Docker Unix endpoint, free loopback fixture ports, exact project/container identity, explicit fixture marker, exact bind mount and loopback 9092 publication. Adversarial tests reject production endpoint/project, wrong container/mount/public binding, missing marker (including Python optimization) and unsupported down/restart/delete/reset operations. All stop/start operations in this task use the disposable broker control bridge. Cleanup stops only this unique project's five containers and retains volumes/messages/data; no global prune or `down -v`.

Only the fixture's alert stabilization/evaluation durations are accelerated (`for:10s`, group interval 5s); expressions and thresholds are retained. Offline rule tests verify production `for` durations unchanged. A healthy readiness body is an observation of recorded worker state, not an active broker probe; an idle broker outage may precede a recorded producer failure. Degraded HTTP 200 is the existing policy, not complete dependency-health proof.

Runbooks inspected: [Kafka operations](azure-production/KAFKA_OPERATIONS.md), [observability](azure-production/KAFKA_OBSERVABILITY.md), [TASK 07 Jira acceptance](JIRA_KAFKA_ACCEPTANCE_REPORT.md) and [TASK 08 observability report](KAFKA_OBSERVABILITY_FINAL_REPORT.md). Existing distinction between read-only `--check` and separately authorized `--restart`/`--smoke` is preserved. No live runbook command was executed.

## Reproduce safely on macOS with Docker Desktop

1. Use this worktree, .NET 10 SDK, Python 3 and OpenSSL; start Docker Desktop. Keep production environment files out of the fixture. Do not export Azure endpoints into fixture variables.
2. Confirm loopback ports 19092, 13307, 19090 are free; the runner rejects occupied ports. Resolve ownership separately instead of stopping unrelated containers.
3. Run guard/rule checks, then the isolated runner:

   ```bash
   python3 tests/Kafka/test_broker_control.py
   ./scripts/test-kafka-observability.sh
   ./scripts/test-kafka-integration.sh
   ```

4. Expect eight safety tests; 159 checks/33 scenarios/55 expressions; five real integration tests with GH 3/Jira 2, zero skips. The GH flow additionally verifies 128 prepublished records across restart. Runtime is normally several minutes; keep the complete log and failed-run artifacts too.
5. Reproduce policy regressions separately (no real broker needed):

   ```bash
   env -u RT_KAFKA_TEST_ENABLE dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingTests
   env -u RT_KAFKA_TEST_ENABLE dotnet test tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj -c Release --filter FullyQualifiedName~KafkaMessagingTests
   ```

6. Inspect the run-specific TRX, both new retention JSON files, final topic/group output and business/metric artifacts. Missing prerequisites/daemon means NOT EXECUTED; skipped tests or absent final evidence never mean PASS.
7. Preserve retained data when the runner stops its unique project. Do not reset offsets, rewrite rows, delete topics/volumes or clear events to repeat a failed phase. Start a fresh runner-owned fixture for a reproducible new run.

## Unverified scenarios and follow-up

- **NOT VERIFIED:** complete ten-attempt producer exhaustion using native timeouts, real MySQL and wall-clock production backoffs. Deterministic store coverage is retained; a separately scheduled long disposable run is needed for full transport proof. Do not shorten production delays or repair `NextAttemptAtUtc` to claim that proof.
- **NOT VERIFIED:** independent Jira held-consumer scenario and Jira 128-record retained backlog; shared behavior is exercised through GH, and Jira's own commit-failure replay survives broker restart.
- **NOT VERIFIED:** a retained batch of distinct event identities, concurrent multi-replica handoffs, heavy load, process/host/power/disk failures, and data beyond retention. The single-broker/RF1 fixture establishes ordinary restart recovery only.
- **NOT VERIFIED:** every application/business API route during outage, complete hosted-service startup wiring under Kafka-enabled production configuration, real provider webhooks and deployed metrics. Flow HTTP hosts deliberately remove application hosted workers to avoid a second scheduler; they retain the real HTTP pipeline/MySQL and attach the existing health object used by the tested workers.
- **NOT VERIFIED:** actual firing/recovery of all ten alert types in the real fixture and real 10-minute processing-window expiry. Synthetic promtool coverage is complete; not every fault type is induced live.
- **NOT VERIFIED:** Azure production recovery or rollout. No production chaos test is proposed; use the existing separate approved read-only deployment/provider acceptance procedures instead.

When a real isolated test fails, retain its run ID/log/TRX, phase JSON and metadata, distinguish TLS/connectivity errors from outbox schedule/lease state, and inspect receipt/commit boundaries. Do not reinterpret missing evidence as loss-free recovery or reset the state to silence lag. Permanent failed/held work needs reviewed diagnosis; recovery within the retry budget and recovery after exhaustion are different policies.

## Changed files and completion

TASK 09 changes:

1. `tests/Kafka/IsolatedKafkaFixture.cs` —bounded retained-batch restart inspection/evidence helper. Prior TASK 06/07 changes in this already-dirty file remain.
2. `tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs` —extend existing 128-record held/replay case with persistence, contiguous record and committed-offset assertions.
3. `docs/devops/KAFKA_FAILURE_RECOVERY_FINAL_REPORT.md` —this 20-requirement matrix, actual results and evidence/limitations.

No worker behavior, topic contract, migration, TLS configuration, Compose, runner, production workflow or operational command policy was changed. All work is local; no commit, push, deployment, production restart or resource/secret mutation occurred.

- [x] All 20 requirements mapped to concrete tests and scope limitations.
- [x] Existing five isolated tests rerun and executed with zero skips.
- [x] Already-published backlog survives ordinary restart and all 128 records replay exactly without offset/data repair.
- [x] Durability, reconnect, commit/rejoin, duplicate protection, business outcome, readiness, metrics and selected alerts verified locally.
- [x] Bounded retry/exhaustion policy and fixture safety regressions executed.
- [x] Logs/artifacts retained and remaining scenarios explicitly NOT VERIFIED.
- [ ] Long wall-clock producer exhaustion and remaining service/host-specific scenarios verified.
- [ ] Separately approved production read-only acceptance evidence retained; production chaos is excluded.
