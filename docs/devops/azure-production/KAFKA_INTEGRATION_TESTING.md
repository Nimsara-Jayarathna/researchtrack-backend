# Kafka application integration testing and live evidence

Updated 2026-10-10. Local contract/implementation approval does not authorize production
writes, workflow dispatch, synthetic provider activity, secrets, deployment or restart.
This guide prepares reviewable actions; none of the live steps below ran in this session.

## Available local checks

Run from repository root:

```bash
dotnet restore ResearchTrack.sln --verbosity minimal
dotnet build ResearchTrack.sln --configuration Release --no-restore
dotnet test ResearchTrack.sln --configuration Release --no-build \
  --filter 'Category!=DatabaseIntegration' \
  --logger 'trx;LogFilePrefix=kafka-task' --results-directory artifacts/kafka-task
PYTHONDONTWRITEBYTECODE=1 python3 deploy/azure/validation/test_kafka_topics_config.py
bash deploy/azure/validation/test-kafka-operations.sh
bash deploy/azure/validation/test-kafka-smoke.sh
bash deploy/azure/validation/test-network-probe.sh
bash deploy/azure/validation/test-validate-azure-env-files.sh
bash deploy/azure/validation/test-service-env-composition.sh
shellcheck scripts/test-kafka-integration.sh deploy/azure/vm/scripts/kafka-topics.sh
actionlint .github/workflows/kafka-integration-tests.yml
bash scripts/test-kafka-integration.sh
```

The non-Docker tests use relational SQLite and actual service ingress/handlers/workers
with synthetic provider clients. They verify atomic inbox/outbox persistence, duplicate
identity, lease recovery, bounded retry retention, rollback, strict schema/version/key,
manual commit policy, disabled behavior and GitHub's actual mirror/commit outcome.
They do not prove a broker acknowledged a message. SQLite timestamp conversion is confined
to test fixtures. Production uses MySQL; migrations were generated and statically checked,
not applied to any production database.

## Disposable real Kafka/MySQL fixture

Prerequisites: Docker daemon + Compose, .NET 10, OpenSSL and Python 3; loopback ports 19092
and 13307 free. The script fails with exit 2 and **NOT EXECUTED** before provisioning if
Docker is unavailable. That is the observed result on this machine on 2026-10-10.
Do not count conditional test skips as integration success.

`tests/Kafka/compose.yml` is a separate, disposable test fixture, never a production broker.
The script uses a unique Compose project and temporary directory, a two-day synthetic CA,
verified localhost/IP-SAN TLS Kafka 3.9.1 on 127.0.0.1:19092 and MySQL 8.4 on 127.0.0.1:13307.
Only the test broker's protected keystore/config and public CA are mounted; the CA private
key remains outside mounts. Application clients retain certificate and endpoint verification.
Disposable MySQL uses loopback-only access and synthetic credentials; it is not the
production TLS policy. No real env files or provider credentials are read.

Only the two approved application topics are explicitly created, with one partition,
RF1 and 72h retention; auto-create is disabled. The fixture endpoint/database guard refuses
Azure endpoints. A trap removes only its unique project/volume and temporary directory.
If startup/test fails, results must be FAIL or NOT EXECUTED, never a mocked fallback pass.

Three conditional tests run with `Category=KafkaIntegration`:

- GitHub: signed ingress → actual MySQL inbox/outbox → real TLS producer acknowledgement →
  approved topic/consumer → production handler and receipt/commit → existing delivery worker,
  event processor and repository synchronization → expected mirror head SHA/commit. New
  clients verify committed offsets and replay the same event with no extra sync run.
- GitHub outage: real native producer targets an unavailable loopback port, reaches its
  publication timeout, and retains the accepted inbox/outbox for durable retry (SQLite
  persistence in this failure-only fixture).
- Jira: authenticated ingress → MySQL/outbox → real TLS acknowledgement → fetched but
  uncommitted record → new consumer replay → actual project scheduling lock/receipt/job
  transaction → existing JiraSyncWorker and JiraSyncService → snapshot issue/revision.
  Duplicate replay and new clients verify receipt/job deduplication and broker offsets.

Provider HTTP/client responses are stubbed, while the Kafka transport and business
processing code are real. MySQL databases start fresh using EnsureCreated: this is not a
migration-upgrade test. The fixture exercises client/handler restart boundaries, not a full
Container Apps revision restart or broker restart. Pending outbox crash-lease recovery is
covered locally; production process restart still requires the separate evidence below.

The manual-only workflow `.github/workflows/kafka-integration-tests.yml` runs the same
script on Ubuntu with Docker and uploads `artifacts/kafka-integration/*.trx` for 14 days.
It has contents-read permission, no production environment/secrets/OIDC or deployment
permission. It was not dispatched. GitHub requires a workflow to be published appropriately
(on the default branch for workflow_dispatch UI discovery); arrange a reviewed publishing
plan because pushing this task branch can trigger production CD. This isolated workflow
is not an Azure integration success report.

## Azure preparation through the existing approved Actions context

1. Review code, generated additive migration SQL and isolated test results. The production
   CD branch is `devops/sprint4-kafka-infrastructure-verification`; publishing requires the
   owner's separate decision because a push may deploy. Preserve existing environment
   approvals, OIDC and concurrency. Resolve existing OIDC/bootstrap prerequisites in the
   runbook without changing identities as part of this task.
2. With deployment separately authorized, deploy application code/additive migrations using
   existing production CD while `Kafka__Enabled=false`. Migration Up adds only the outbox
   and receipt tables/indexes in each service database. Review schema state and backup policy
   with database owners; no schema or data was changed in this session.
3. Review topic reconciliation dry run/check through an authorized Actions VM Run Command
   step using the existing production resource group and VM context. These commands are
   VM-side command text, not instructions to SSH:

   ```bash
   /opt/researchtrack/scripts/kafka-topics.sh reconcile --dry-run
   /opt/researchtrack/scripts/kafka-topics.sh reconcile --check
   ```

   If missing topics are confirmed, obtain explicit production topic-write authorization
   before a reviewed manual-only Run Command step executes:

   ```bash
   /opt/researchtrack/scripts/kafka-topics.sh reconcile --apply
   /opt/researchtrack/scripts/kafka-topics.sh reconcile --check
   /opt/researchtrack/scripts/kafka-topics.sh describe researchtrack.github.events.v1
   /opt/researchtrack/scripts/kafka-topics.sh config researchtrack.github.events.v1
   /opt/researchtrack/scripts/kafka-topics.sh describe researchtrack.jira.events.v1
   /opt/researchtrack/scripts/kafka-topics.sh config researchtrack.jira.events.v1
   ```

   The existing routine infrastructure workflow does not apply the registry. Reuse its
   approved Azure login/VM Run Command context for the reviewed manual step; do not add
   apply to routine deployment. Capture PASS/FAIL, effective topic settings and timestamp.
   Missing/drift returns failure; never delete, repartition or overwrite an existing topic.
4. Obtain the **existing broker public CA** from `/data/researchtrack/kafka/tls/ca.crt` through
   the approved context. Never export CA/broker private keys or generate replacement trust.
   Confirm fingerprint, expiry, broker SAN/private endpoint and effective network restrictions.
   Base64-encode only that public certificate. Operator fills the existing multiline
   `RT_GITHUB_SERVICE_ENV` / `JIRA_ENV_FILE` secrets with the approved topic, version, group,
   SSL verification fields and actual endpoint/CA; no new secret names are needed.
5. After isolated checks pass and the above live preparations are approved, operator enables
   Kafka in the two service bundles and uses the existing protected CD pipeline. Inspect
   revision readiness and safe logs. Readiness alone is not live producer/consumer proof.

No direct Azure action or production synthetic workflow was implemented/executed here.
Production synthetic project/repository identities and credentials have not been supplied.
The isolated manual workflow cannot be pointed at Azure. Actual topic provisioning and
synthetic live events must use a separately reviewed manual step in the existing production
Actions environment, retaining its deployment permissions and approvals.

## Actual application-topic PASS/FAIL evidence

Use only separately authorized synthetic provider resources. Do not publish guessed JSON
directly to production: the consumer validates an owned, immutable outbox created at the
service's authenticated entry point.

For GitHub, create a signed supported push delivery for a linked synthetic repository with
a known commit SHA and installation. For Jira, send an authenticated matched issue update
for a connected synthetic project with a known issue summary and expected revision change.
Use existing provider/webhook authorization; do not weaken signatures or JWT validation.

Capture each chain, with UTC timestamp and environment/revision:

| Evidence boundary | Required result |
|---|---|
| Authentic ingress | Accepted delivery and one database inbox/outbox with stable eventId |
| Publisher | `Kafka outbox acknowledged` correlation GUID and PUBLISHED row |
| Broker/subscriber | Actual approved topic, correct key/version, consumer group and partition/offset |
| Durable handoff | `Kafka durable handoff and offset commit completed`, durable receipt/hash/outcome, committed next offset |
| Business worker | Existing inbox/job PROCESSED/COMPLETED and normal retry state |
| Actual outcome | GitHub mirror/head SHA and sync evidence; Jira issue snapshot and sync revision |
| Duplicate | Same provider delivery/eventId produces no new outbox or extra logical processing |
| Final report | PASS only when all boundaries hold; otherwise FAIL with the first missing boundary |

Existing polling can complete business work before Kafka arrives. A later `already_processed`
receipt proves safe transport replay but does not prove Kafka caused processing. For a
causality test, use an isolated authorized verification environment with controlled worker
startup as the test fixture does, or collect handoff-before-worker timestamps. Never infer
GitHub/Jira completion from the deployment-smoke token alone.

Preserve safe correlation GUID/offset logs and restricted database/API evidence as Actions
artifacts or approved records. Do not include real webhook bodies, env files, credentials,
provider tokens or private keys. The existing operational smoke/restart/network evidence
remains useful for transport/persistence but is separate from these application flows.

## Application restart verification (separate acceptance)

Only after explicit restart authorization and a maintenance plan, through the existing
protected Actions deployment context:

1. Record stable eventId, topic settings, committed next offset, outbox status, receipt count,
   inbox/job state and mirror SHA/revision for each approved synthetic flow.
2. Arrange a controlled pending outbox/uncommitted delivery scenario in the approved test
   environment. Restart only the affected application revision; do not restart the broker,
   unrelated apps or re-create topics. Wait for the bounded outbox lease if interrupted.
3. Verify the same durable event is acknowledged/replayed, offsets resume correctly, exactly
   one durable receipt remains and business state completes without an extra logical effect.
4. Re-read actual application topic metadata/settings and confirm they persist unchanged.
   Kafka metadata and volumes are not modified by application startup.
5. Save before/after correlation, revision, offsets and business-state evidence. Invalid
   contracts or exhausted handlers must hold without committing; repair/restart requires
   operator review, not offset skipping. Label missing evidence FAIL/NOT EXECUTED.

No actual application restart, consumer process restart in Azure, broker restart or production
pending-event exercise was performed. Existing broker-only restart verification is documented
in [operations](KAFKA_OPERATIONS.md) and requires separate approval.
