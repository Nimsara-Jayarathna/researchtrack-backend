# Sprint 4 Kafka topics and GitHub/Jira end-to-end integration — task report

2026-10-10. **Approved application implementation is complete locally. Actual broker,
Azure application flow and application restart acceptance remain unverified.**
Real producers/consumers replace the previous configuration-only readiness implementation.
The owner explicitly approved the complete same-service routing contracts in this session;
[approval record](KAFKA_CONTRACT_PROPOSAL.md) and [actual architecture](KAFKA_TOPICS_EVENT_FLOWS.md)
are synchronized with source. Kafka remains disabled by default.

## A. Initial architecture analysis

Initial backend HEAD was `ab2996cd733fbde598254d9a80b495d0b5725aa8` on
`devops/sprint4-kafka-infrastructure-verification`. Only the GitHub/Jira example env files
had previous local edits. Frontend was not modified. Read-only origin/main comparison
showed 11 main-only and 6 branch-only commits; the main-only additions were unit/mock/
SQLite/coverage work, not Kafka contracts or clients. No merge/rebase was performed.

Inspected requested broker Compose/listener/storage/identity/TLS scripts, registry/CLI,
network probes, environment validators/renderers, Azure deployment/workflows and both
services' controllers, ingress, authentication, persistence, schedulers/workers, migrations,
DI and tests before implementation. Accessible read-only Jira search found infrastructure
[SCRUM-198](https://researchtrack.atlassian.net/browse/SCRUM-198), without an application
contract. Rovo reported partial-source warnings; the search was not exhaustive proof that
no agreement existed elsewhere. The owner's explicit approval resolved this decision.

Original production broker: single Apache Kafka 3.9.1 KRaft VM process, private verified TLS
9092, internal Docker plaintext 29092, unexposed controller 9093, persistent data/cluster ID,
RF1. Existing smoke checks used only the deployment-smoke topic. Existing configuration
could validate/deliver public trust but contained no real Kafka messaging clients.
GitHub already had signed ingress → durable delivery inbox → lease/retry delivery worker →
event processor → repository synchronization. Jira already had bearer authentication →
durable webhook records → coalesced persistent sync jobs → worker → transactional snapshot.
Existing HTTP APIs, frontend and polling/reconciliation remain the business access path.

## B. Gaps resolved and remaining

Resolved: agreed contracts/receiving behavior, real SDK/producer/subscriber, atomic outbox,
durable deduplication/handoff, manual offsets, bounded retry/lease recovery, safe readiness,
additive migrations, real-service tests, approved topic registry/groups and synchronized docs.

Remaining verification: Docker daemon unavailable; all three real-client/broker integration
cases are implemented but NOT EXECUTED. No production endpoint/CA, topic state, delivered
runtime config, application correlation chain or restart was inspected. CA remains blank
in examples because only the existing broker public certificate can supply real trust.
The prior unrelated k6 Jira warm-up failure was not changed or hidden.

## C. Features implemented

- Confluent.Kafka 2.16.0 centrally managed and linked shared source only in GitHub/Jira.
  Disabled mode creates no Kafka clients or new outbox events and retains existing behavior.
- Strict version-1 reference JSON, stable eventId/correlation, service-owned identities and
  keys; no raw webhook bodies, tokens, signing secrets or authorization headers in Kafka.
- Atomic accepted inbox + immutable outbox. Lease/CAS claim, per-key pending ordering,
  30-second publish timeout, idempotence/acks-all, ten bounded durable attempts, retained
  FAILED rows and recoverable crash window. No automatic data deletion or DLQ.
- GitHub receipt/owned durable inbox + existing worker pulse; Jira receipt and coalesced job
  in one transaction under the existing project scheduling lock. Durable payload-hash dedup.
- Consumer commits only after durable handoff, retries handoff three times, holds invalid/
  exhausted work without committing, closes/rejoins on commit failure and closes at shutdown.
- Two-table additive migration per service. Stale prior snapshots were corrected to the
  current model; no old migration, existing table or datetime precision was changed by Up.
- Disabled examples now contain approved topic/version/group; enabled validation requires
  the exact group. Existing secretRef/public-CA pipeline reused, with TLS verification intact.
- Disposable loopback TLS Kafka/MySQL test runner and manually invoked isolated Actions
  workflow. No production workflow trigger or security approval bypass was introduced.
- Relational tests cover real entry-point authentication, atomic rollback, duplicate IDs,
  strict contracts, outbox leases/order/exhaustion, post-handoff offsets, durable readiness
  and actual GitHub business mirror processing with synthetic provider clients.
- Database outbox/receipt retention and pending-row replay require an owner policy before
  enablement; tables are durable and currently have no automatic pruning/backfill.

## D. Changed files

The full file inventory relative to task-start HEAD appears at the end of this report.
HEAD advanced externally during work to `e37b154475e8405e1de9dcf42288c91d83874d5f`;
that existing commit includes the initial implementation. The agent did not create it or
reset it. Current follow-up edits remain unstaged. Two generated Python bytecode files
included in that external commit are removed locally, and bytecode is now ignored.

## E. Actual event-flow architecture

GitHub signed ingress → atomic inbox/outbox → real Kafka producer → GitHub topic → actual
GitHub consumer → owned durable receipt/inbox → existing delivery worker/event processor →
repository mirror/head SHA. Jira authenticated matched ingress → atomic inbox/outbox →
real producer → Jira topic → Jira consumer → atomic receipt/coalesced sync job → existing
JiraSyncWorker/JiraSyncService → issue snapshot/revision. Existing polling/scheduling remains
recovery. Transport acknowledgement is separate from business completion.
See the diagram, precise event selection and transactional boundaries in the flow guide.

## F. Topic definitions

| Topic | Owner / consumer | Group | Version | Partitions / RF / retention |
|---|---|---|---|---|
| `researchtrack.deployment-smoke` | DevOps CLI | None; direct partition/offset | 1 | 1 / 1 / 24h |
| `researchtrack.github.events.v1` | GitHub team / GitHubService | `researchtrack-github-webhook-v1` | 1 | 1 / 1 / 72h |
| `researchtrack.jira.events.v1` | Jira team / JiraService | `researchtrack-jira-webhook-v1` | 1 | 1 / 1 / 72h |

All are approved in source. No live creation/check ran. CLI reconciliation remains explicit,
non-destructive and drift-rejecting; application startup does not provision topics.

## G. Environment variables and deployment delivery

Canonical ten keys remain: `Kafka__Enabled`, `Kafka__BootstrapServers`,
`Kafka__SecurityProtocol`, `Kafka__SslCaLocation`, `Kafka__SslCaCertificateBase64`,
`Kafka__EnableSslCertificateVerification`, `Kafka__SslEndpointIdentificationAlgorithm`,
`Kafka__Topic`, `Kafka__ContractVersion`, `Kafka__ConsumerGroupId`.
Defaults: false, example default private endpoint (verify actual deployment), SSL,
`/tmp/researchtrack-kafka/ca.crt`, blank existing public CA, true, https, approved service
topic, version 1, approved nonempty service group. Application source has no production IP
or topic name. Endpoints and trust stay environment-specific.

Existing `RT_GITHUB_SERVICE_ENV` / `JIRA_ENV_FILE` → validation/shared-auth composition →
renderer secretRef → secure deployment parameters → .NET atomic verified mode-0600 CA file.
The renderer test verifies all ten keys for app and job render paths and secret protection.
No new Azure permission/secret name, Dockerfile, OIDC or network change is needed. Actual
Container Apps delivery was not observed. Keep disabled until the operator has broker
trust, actual topics, test results, capacity policy and separate enablement authorization.

## H. Exact checks and observed results

Commands run from repository root. `EF` below is the temporary tool executable
`/tmp/researchtrack-kafka-ef.OLfEGr/dotnet-ef`, installed with `dotnet tool install dotnet-ef
--version 10.0.9 --tool-path /tmp/researchtrack-kafka-ef.OLfEGr`. No global or repo tool config
was modified. Artifacts are local/ignored, not production evidence.

| Command | Actual outcome |
|---|---|
| `dotnet restore ResearchTrack.sln --verbosity minimal` | PASS; final restore all up-to-date. Initial NU1903 SQLite native vulnerability was resolved using audited package pins; transient native-client download retried successfully |
| `dotnet build ResearchTrack.sln --configuration Release --no-restore -v minimal` | PASS, 0 warnings/errors |
| `dotnet test ResearchTrack.sln --configuration Release --no-build --filter 'Category!=DatabaseIntegration' --logger 'trx;LogFilePrefix=kafka-task-final' --results-directory artifacts/kafka-task` | PASS: 360 passed, 3 Docker-conditional skipped, 0 failures. GitHub 196+2 skip; Jira 61+1 skip; Auth 15, Gateway 24, Project 9, Meeting 27, Submission 28 |
| `dotnet test tests/ResearchTrack.GitHubService.Tests --configuration Release --no-build --filter FullyQualifiedName~KafkaMessagingTests` | PASS: 15 new messaging cases; full final suite also includes the real GitHub business-worker outcome test |
| `dotnet test tests/ResearchTrack.JiraService.Tests --configuration Release --no-build --filter FullyQualifiedName~KafkaMessagingTests` | PASS: 10 new messaging cases; final suite verifies updated fixtures |
| `python3 deploy/azure/validation/test_kafka_topics_config.py` | PASS: 16 methods, including synthetic approval gate, exact consumer group, all env keys/CA secret delivery and shared-source image impact |
| `bash deploy/azure/validation/test-kafka-operations.sh` | PASS: 43 offline checks; updated final bundle assertion for owner-approved app contracts, with no automatic apply preserved |
| `bash deploy/azure/validation/test-kafka-smoke.sh` | PASS: 9 mocked cases; not live transport |
| `bash deploy/azure/validation/test-network-probe.sh` | PASS: 12 offline checks; not ACA connectivity evidence |
| `bash deploy/azure/validation/test-validate-azure-env-files.sh` | PASS: 22 synthetic configuration checks |
| `bash deploy/azure/validation/test-service-env-composition.sh` | PASS: 10 checks; seven services + Jira migration + missing/duplicate JWT protections |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS: 11 synthetic PLAN_ONLY checks, no real deploy |
| `bash scripts/test-kafka-integration.sh` | NOT EXECUTED, exit 2: Docker daemon unavailable; no fixture/prod container started |
| `RT_KAFKA_TEST_UID=1000 RT_KAFKA_TEST_GID=1000 RT_KAFKA_TEST_CLUSTER_ID=synthetic RT_KAFKA_TEST_PASSWORD=synthetic RT_KAFKA_TEST_WORK=/tmp/researchtrack-synthetic docker compose --env-file /dev/null --project-name rt-kafka-static-validation -f tests/Kafka/compose.yml config --quiet` | PASS, syntax/config only; synthetic values |
| `shellcheck scripts/test-kafka-integration.sh deploy/azure/vm/scripts/kafka-topics.sh` | PASS |
| `shellcheck deploy/azure/vm/scripts/verify-kafka.sh` | Existing SC1091/SC2016/SC2015 informational findings in unchanged file, exit 1 |
| `shellcheck -e SC1091,SC2016,SC2015 deploy/azure/vm/scripts/verify-kafka.sh` | PASS with those documented baseline exclusions |
| `actionlint .github/workflows/kafka-integration-tests.yml` | PASS |
| `actionlint` | Existing queue syntax/tool compatibility and SC2129 findings in unchanged production/performance/runtime workflows, exit 1 |
| `actionlint -ignore 'unexpected key "queue" for "concurrency" section' -ignore 'SC2129'` | PASS with those documented baseline exclusions; workflow files unchanged |
| `bash -n` individually for test runner, all VM `*kafka*.sh` and validation `*kafka*.sh` | PASS, 7 files; loop executed via Python subprocess, not a multi-path bash command |
| Python `compile(Path(file).read_text(), file, 'exec')` for Kafka config/test modules | PASS, 2 files without generated bytecode |
| `EF migrations has-pending-model-changes --project src/Services/ResearchTrack.GitHubService --configuration Release --no-build` | PASS, model current |
| Same command for `ResearchTrack.JiraService` | PASS, model current |
| `EF migrations script 20260923043000_AddGitHubSyncRevision 20261010151156_AddKafkaWebhookMessaging --project src/Services/ResearchTrack.GitHubService --configuration Release --no-build --output artifacts/kafka-task/GitHub-kafka-migration.sql` | PASS; inspected bounded upgrade SQL only new tables/indexes, no existing ALTER/DROP |
| Same script command for Jira from `20260923043100_AddJiraSyncRevision`, Jira project and Jira output | PASS; same additive-only result; no SQL executed against any DB |
| `git diff --check` / `git diff --cached --name-only` / frontend `git status --short` | PASS whitespace; index empty; frontend clean |

Earlier failures corrected before final results: conditional xUnit skips initially lacked
skip messages; shared in-memory SQLite connection caused a concurrent worker lock error;
fixture now uses separate connections and a private WAL database; old operations bundle
asserted that topics were unapproved; assertion now verifies the owner-approved contracts.
Initial migration generation picked up stale snapshot business-table/precision drift; final
Up excludes those operations and both model checks pass. An initial EF invocation used `-c`
(context alias) instead of `--configuration`; corrected Release command succeeded. Idempotent
SQL was also generated for static inspection, then replaced by the bounded normal upgrade
script above; production uses existing EF migration execution, not manual script application.
The current task did not run `test_service_impact.py` because it stages/commits temporary
fixture repositories; new Kafka tests verify linked-source image impact read-only instead.
External SQL DatabaseIntegration cases requiring existing database services were excluded.

## I. Deployment and restart verification

[Integration testing guide](KAFKA_INTEGRATION_TESTING.md) documents exact isolated runner,
manual-only Actions workflow, reviewed topic commands in the existing approved VM Run
Command context, unchanged secret delivery/CD, synthetic provider correlation chain and
separate application restart acceptance. No workflow was dispatched. Actual production
synthetic IDs/credentials were not supplied, so no production event sender was fabricated.
The isolated workflow has no production secrets/environment/OIDC and cannot verify Azure.

Local restart evidence: expired outbox lease recovery with stale-owner rejection; new durable
handler instances deduplicate; CA file re-delivery is atomic. Real-client tests additionally
implement offset continuity, fetched-uncommitted replay (Jira), duplicate replay and topic
metadata persistence; those tests are unexecuted. Full application revision/broker restart,
production topic persistence and pending-event recovery remain NOT EXECUTED.

## J. Exact seven-item Definition of Done

| Requirement | Implementation status | Changed files | Verification command | Actual result | Production verification | Evidence location | Remaining blocker | Responsible team |
|---|---|---|---|---|---|---|---|---|
| 1. Required ResearchTrack topics exist. | IMPLEMENTED — LIVE VERIFICATION REQUIRED | topics.json; registry validator; operations regression | `kafka-topics.sh reconcile --check` in approved Actions VM context | Source validates; offline reconciliation passes; live command not run | NOT EXECUTED | Registry; 16 Python methods; operations results | Authorized deployed manifest/topic check and explicit apply if missing | DevOps |
| 2. Producer/consumer connectivity is configured. | IMPLEMENTED — LIVE VERIFICATION REQUIRED | KafkaClients; RuntimeOptions; DI; env examples | Isolated runner, then actual enabled revision/flow | Client config/trust tests pass; real broker fixture not executed | NOT EXECUTED | .NET TRX; env renderer tests | Docker execution, actual broker CA/endpoint and authorized enablement | GitHub/Jira development + DevOps |
| 3. Agreed GitHub Kafka flow is demonstrated. | IMPLEMENTED — LIVE VERIFICATION REQUIRED | GitHub ingress/store/handler; shared messaging; GitHubKafkaFlowTests | Isolated runner GitHub category; authorized synthetic push correlation | Real business worker mirror test passes; real transport tests skipped | NOT EXECUTED | GitHub TRX; flow test source | Real broker ack/consumer/business chain and production synthetic resource approval | GitHub team + QA + DevOps |
| 4. Agreed Jira Kafka flow is demonstrated. | IMPLEMENTED — LIVE VERIFICATION REQUIRED | Jira ingress/scheduler/handler; shared messaging; JiraKafkaFlowTests | Isolated runner Jira category; authorized matched synthetic update | Authenticated outbox/receipt/job tests pass; real transport/snapshot test skipped | NOT EXECUTED | Jira TRX; flow test source | Real MySQL lock/Kafka/snapshot execution and production synthetic resource approval | Jira team + QA + DevOps |
| 5. Topic ownership/purpose is documented. | COMPLETE | Approved contract; registry; event-flow guide | Registry tests + source/doc review | Owners, groups, keys, schema, retention and purpose synchronized | Documentation requirement satisfied locally | Contract approval and topic matrix | None for documentation | GitHub/Jira owners + DevOps |
| 6. Environment-specific values are externalized. | COMPLETE | Examples; RuntimeOptions; kafka_config.py; renderer tests | Python config/env tests + .NET config tests | All ten keys, endpoint/trust, approved groups verified; no source production IP/topic | Source/config requirement satisfied; actual delivery separate | Config examples and test results | None for externalization; real CA needed for enablement | DevOps + service owners |
| 7. Configuration is integrated into deployment. | IMPLEMENTED — LIVE VERIFICATION REQUIRED | Existing env/secret renderer reused; exact-group validator; service projects/migrations | Renderer/composition/image-impact tests; approved production CD afterward | Offline delivery/hash/composition passes | NOT EXECUTED | Config tests; deployment trace; migration SQL | Authorized deploy and runtime trust/config evidence | DevOps + DB owners |

No flow demonstration is marked COMPLETE from a mock, receipt alone, configuration test or
historical deployment-smoke evidence. Application restart verification is explicitly separate
and remains pending, as described above.

## K. Required manual actions

1. Run disposable real integration tests on a machine with Docker or the separately published
   manual isolated workflow; retain actual TRX/PASS results. Review retention/replay capacity.
2. Owner reviews/publishes changes deliberately; this branch can automatically deploy.
3. Operator separately authorizes additive migration deployment, actual topic dry-run/check
   and creation of approved missing topics, using existing protected Actions context.
4. Supply existing broker public CA and actual endpoint; update existing service env secrets
   only with authorization, then enable using existing CD protections.
5. Use authorized synthetic provider resources, collect each actual Kafka and business
   boundary, then authorize/collect separate application restart and pending-work evidence.

## L. Git safety

No agent staging, commit, push, merge, rebase, PR creation, workflow dispatch, Azure write,
production topic write, secret update or restart occurred. A read-only git fetch was used;
HEAD moved externally from ab2996c to e37b154 and is preserved. Main divergence is now 11/7,
with the one additional branch commit already present. Index empty; frontend unchanged.
Two generated tracked bytecode files from the external commit are deleted in the current
local diff; no source or credentials were removed. Real env files/private credentials were
not inspected. Pushing this branch may automatically deploy production.

## Full changed-file inventory relative to task start

52 source/config/docs/test files changed across this task (including files already
in the externally recorded e37b154 commit). All remaining changes are local.

- `.github/workflows/kafka-integration-tests.yml` — manual isolated Kafka/MySQL test workflow and TRX artifacts.
- `.gitignore` — exclude generated Python bytecode.
- `Directory.Packages.props` — centrally pin Kafka client and audited relational test dependencies.
- `config/env/README.md` — enabled/disabled messaging and CA configuration instructions.
- `config/env/github/.env.example` — approved service topic/version/group, verified SSL and disabled defaults.
- `config/env/jira/.env.example` — approved service topic/version/group, verified SSL and disabled defaults.
- `deploy/azure/scripts/kafka_config.py` — require exact approved consumer group for enabled messaging.
- `deploy/azure/validation/test-kafka-operations.sh` — verify bundled owner-approved topic contracts and no automatic apply.
- `deploy/azure/validation/test_kafka_topics_config.py` — approval/transport/env rendering/image-impact regressions.
- `deploy/azure/vm/kafka/topics.json` — approved topic ownership, schemas, groups and settings.
- `deploy/azure/vm/scripts/kafka-topic-manifest.jq` — strict names/whitespace and fixed smoke invariants.
- `docs/devops/azure-production/IMPLEMENTATION_STATUS.md` — current evidence and remaining live verification.
- `docs/devops/azure-production/KAFKA_CONTRACT_PROPOSAL.md` — complete owner-approved event/processing contract.
- `docs/devops/azure-production/KAFKA_INTEGRATION_TESTING.md` — exact local tests and authorized Actions/restart evidence procedure.
- `docs/devops/azure-production/KAFKA_OPERATIONS.md` — connect broker operations to implemented messaging.
- `docs/devops/azure-production/KAFKA_TASK_REPORT.md` — retain historical infrastructure evidence and link current follow-up.
- `docs/devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md` — actual architecture, lifecycle, schema, reliability and configuration.
- `docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md` — initial gaps, results, exact seven-item DoD and file inventory.
- `docs/devops/azure-production/README.md` — navigation to current contracts/tests/flows.
- `scripts/test-kafka-integration.sh` — disposable loopback verified-TLS Kafka/MySQL fixture runner.
- `src/BuildingBlocks/Kafka/KafkaClients.cs` — real Confluent factory/publisher, TLS and bounded client configuration.
- `src/BuildingBlocks/Kafka/KafkaMessagingWorkers.cs` — outbox/consumer workers, handoff/offset policy, readiness and DI.
- `src/BuildingBlocks/Kafka/KafkaPersistence.cs` — outbox/receipt entities, leases, durable retries and deduplication.
- `src/BuildingBlocks/Kafka/KafkaRuntimeOptions.cs` — validated service-specific contract/group and existing CA delivery.
- `src/BuildingBlocks/Kafka/KafkaWebhookContract.cs` — strict schema/version/key/selection and timestamp normalization.
- `src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubKafkaEventHandler.cs` — owned durable receipt/inbox handoff and existing worker signal.
- `src/Services/ResearchTrack.GitHubService/Features/Webhooks/GitHubWebhookDeliveryStore.cs` — atomic accepted delivery and immutable outbox.
- `src/Services/ResearchTrack.GitHubService/Persistence/GitHubDbContext.cs` — map Kafka persistence; Jira preserves existing datetime(6) precision.
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.Designer.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/GitHubDbContextModelSnapshot.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.GitHubService/Program.cs` — register trusted service options and enabled-only real messaging DI.
- `src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj` — Kafka linked source/client or relational test dependencies.
- `src/Services/ResearchTrack.JiraService/Extensions/JiraFeatureExtensions.cs` — register existing project-lock adapter for Kafka handoff.
- `src/Services/ResearchTrack.JiraService/Features/JiraKafkaEventHandler.cs` — locked atomic durable receipt/coalesced-job handoff.
- `src/Services/ResearchTrack.JiraService/Features/JiraSyncScheduler.cs` — reuse job staging within consumer receipt transaction.
- `src/Services/ResearchTrack.JiraService/Features/JiraWebhookService.cs` — atomic matched webhook/outbox at authenticated acceptance.
- `src/Services/ResearchTrack.JiraService/Persistence/JiraDbContext.cs` — map Kafka persistence; Jira preserves existing datetime(6) precision.
- `src/Services/ResearchTrack.JiraService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.Designer.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.JiraService/Persistence/Migrations/20261010151156_AddKafkaWebhookMessaging.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.JiraService/Persistence/Migrations/JiraDbContextModelSnapshot.cs` — additive two-table Kafka migration and current EF model metadata.
- `src/Services/ResearchTrack.JiraService/Program.cs` — register trusted service options and enabled-only real messaging DI.
- `src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj` — Kafka linked source/client or relational test dependencies.
- `tests/Kafka/IsolatedKafkaFixture.cs` — guarded real loopback client/database setup and bounded consume.
- `tests/Kafka/KafkaTestDatabase.cs` — relational SQLite WAL or isolated MySQL test persistence.
- `tests/Kafka/compose.yml` — separate disposable Kafka/MySQL stack; no production broker.
- `tests/ResearchTrack.GitHubService.Tests/GitHubKafkaFlowTests.cs` — actual existing business processing and guarded real TLS broker integration/restart assertions.
- `tests/ResearchTrack.GitHubService.Tests/KafkaMessagingTests.cs` — service atomicity, identity, duplicates, disabled behavior and shared reliability regressions.
- `tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj` — Kafka linked source/client or relational test dependencies.
- `tests/ResearchTrack.JiraService.Tests/JiraKafkaFlowTests.cs` — actual existing business processing and guarded real TLS broker integration/restart assertions.
- `tests/ResearchTrack.JiraService.Tests/KafkaMessagingTests.cs` — service atomicity, identity, duplicates, disabled behavior and shared reliability regressions.
- `tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj` — Kafka linked source/client or relational test dependencies.

Generated bytecode removed relative to current HEAD: `deploy/azure/scripts/__pycache__/kafka_config.cpython-314.pyc` and `deploy/azure/scripts/__pycache__/render-containerapp.cpython-314.pyc`.
