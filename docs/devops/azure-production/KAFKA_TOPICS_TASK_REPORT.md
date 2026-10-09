# Kafka topics and ResearchTrack event flows — local task report

Date: 2026-10-09. Objective: finish DevOps topic/configuration/trust/deployment support
without inventing application event behavior or changing production.

Backend baseline/current branch: `devops/sprint4-kafka-infrastructure-verification`.
Baseline/current HEAD: `acd1445fb31fc7e445fdf1ea1f5734769bd069f3`.
Frontend reference/current branch: `Deploy-and-Configure-Kafka-Infrastructure`.
Frontend HEAD: `20e6bfc0f916365c0b486ce0f5edbba36acb8240`.
Both trees were clean before editing. Backend changes remain unstaged/uncommitted;
frontend remains unchanged. No remote write, staging/commit in either ResearchTrack repository, push/merge/rebase, PR,
workflow trigger, Azure deployment/resource mutation, live topic write or restart occurred.
Real `.env` files and production credentials were not inspected.

Git-safety exception in testing: the existing `test_service_impact.py` suite creates,
stages and commits disposable temporary fixture repositories. This was identified after
execution; those fixtures were cleaned up and neither ResearchTrack index/HEAD changed.
The suite was not rerun after identifying that behavior. This crossed the literal ban
on staging/commit commands even though implementation changes remain fully unstaged.

## Repository analysis before implementation

| Area | Existing implementation | Gap | Implemented DevOps change |
|---|---|---|---|
| Kafka topics | Existing `kafka-topics.sh`, isolated deployment smoke | No central registry/reconciliation | Registry + validate/dry-run/check/explicit apply |
| GitHub producers | Signed webhook ingress and durable delivery processing | No Kafka package/client/producer/contract | Optional transport settings; exact developer handoff |
| GitHub consumers | Existing inbox delivery and repo sync workers | These are not Kafka consumers; intended consumer unknown | Preserve workers; do not invent group/processing |
| Jira producers | Authenticated webhook records and persistent sync jobs | No Kafka package/client/producer/contract | Optional transport settings; exact developer handoff |
| Jira consumers | `JiraSyncWorker` handles DB jobs | No Kafka subscription or agreed processing contract | Preserve worker; document missing consumer |
| Service config | Existing ASP.NET Core binding; no Kafka section | Endpoint/topic/trust delivery absent | Disabled defaults, bound singleton, actual public CA file |
| Azure deploy | Env secrets → validator → composed env → renderer → secure Bicep parameters | Kafka settings/approval/trust checks absent | Extend validator/renderer, reuse secret delivery/hash |
| Testing | Broker safety/network/env/application tests | No manifest/reconciliation/runtime trust coverage | Offline broker, config/render/Test validator and .NET trust tests |
| Docs | Existing Kafka broker operations/report | No event-flow/ownership guide | Dedicated guide/report and index/status links |

Inspected the requested VM Compose/Kafka/listener/storage scripts, NSG, infrastructure and
backend workflows, probes, bundle builder/installer, deployer/renderer/Bicep, canonical
env contracts/validators and regression tests. Inspected GitHub/Jira controllers, inbox
entities, sync workers/queues/schedulers, persistence, DI/options and test projects. Searched
source/tests/architecture/backlog/config for Kafka clients and agreed contracts. Frontend
GitHub/Jira integrations call existing HTTP APIs; direct Kafka browser access is unnecessary.

Exact current application paths and retries are documented in
[KAFKA_TOPICS_EVENT_FLOWS.md](KAFKA_TOPICS_EVENT_FLOWS.md). Provider webhook DTOs and
inbox entities do not constitute approved Kafka events. The user replied that they will
provide approved contracts, but topic/schema/key/consumer contents have not yet arrived.

## Functionality reused and implementation

- Preserve DEVOPS-4.1 broker image/listeners/private TLS, KRaft identity, persistent storage,
  CA/SAN validation, dedicated smoke implementation and explicit restart/network checks.
- Preserve OIDC, deployment approvals/concurrency, image provenance, shared-auth composition,
  revision hashing, ingress and existing business logic.
- Add `topics.json` and a jq schema validator to the existing CLI. Duplicate/invalid names,
  missing fields, invalid ownership/approval fields, fractional/zero/out-of-range partitions,
  RF other than 1 and nonpositive/indefinite retention fail before broker access.
- Add default dry run, read-only check and explicit apply. Only approved missing topics are
  created with existing idempotent creation; all existing approved topics are inspected first.
  Partition/RF/effective retention drift fails without creation/alteration. Broker failures
  return nonzero. There is no delete or destructive reconciliation path.
- Validate/package the registry and jq helper in the existing VM bundle; validate on install.
  No apply is wired into VM reconciliation, service startup, migrations or application deploy.
- Add common source `KafkaRuntimeOptions.cs`, linked only into GitHub/Jira. Bind normal .NET
  configuration, validate enabled TLS/bootstrap/topic/version, read actual mounted trust or
  atomically deliver a validated public CA with mode 0600, and register only a settings
  singleton. No Kafka SDK, producer, consumer, automatic topic creation or business handler.
- Add optional canonical keys and disabled appsettings. Production validation requires
  private RFC1918 IPv4:9092, approved service-domain topic/version, SSL, CA delivery and
  verification. Supplied groups must match the registry. Unknown keys and unrelated-service
  Kafka settings fail. Disabled and older bundles need no unused credentials or CA.
- Render public CA Base64 using existing Container App secret references/secure parameters.
  Its value participates in the existing revision digest. No Dockerfile/Bicep change is
  required because .NET startup creates the real file before client registration.
- Existing build-impact logic recognizes the linked source: only GitHub/Jira image scopes
  include it. The common API library and solution remain unchanged.
- Backend CI includes the new offline suite/Python syntax check and validates each shell
  file individually (passing several paths to a single `bash -n` checks only the first).

Reconciliation is idempotent, not transactional: partial successful creates remain if a
later broker command fails. Repeat after diagnosis. It reports concurrent administrative
drift without destructive rollback. RF1 provides no multi-broker resilience.

## Topic definitions and application status

| Definition | Approval | Partitions/RF/retention | Contract and application evidence |
|---|---|---|---|
| `researchtrack.deployment-smoke` | Existing operational definition approved | 1 / 1 / 24h | Unique UTF-8 token, DevOps CLI owner/consumer, no app group; current live state not checked |
| `researchtrack.github.events.v1` | PROPOSED, false | 1 / 1 / 72h proposed | Schema/version/key/event/consumer unknown; excluded from all provisioning |
| `researchtrack.jira.events.v1` | PROPOSED, false | 1 / 1 / 72h proposed | Schema/version/key/event/consumer unknown; excluded from all provisioning |

The naming convention is `researchtrack.<domain>.<event-category>.v<major-version>` with
the existing operational name retained. One broker requires RF1. One partition and 72h
are conservative assignment proposals, not agreed application processing decisions.

GitHub Kafka flow: **BLOCKED BY APPLICATION DEVELOPMENT**. Existing webhook processing
remains intact; there is no real Kafka publisher/subscriber or contract-valid Kafka
application test. Settings and CA readiness do not publish anything.

Jira Kafka flow: **BLOCKED BY APPLICATION DEVELOPMENT** for the same reasons. Existing
durable synchronization and tests remain intact. CLI/token checks are transport evidence only.

Developers must provide selected events/sources, payload schema/reference/version, key and
ordering, producer transaction/outbox boundary, consumer service/group, processing result,
retry/idempotency/failure/offset semantics, owners and executable synthetic integration tests.
If another service is an agreed consumer, its actual client/configuration must be inspected
before adding its own enabled env contract; no unrelated service is preconfigured speculatively.

## Deployment and security

The existing GitHub Environment multiline service secret is materialized as `github.env`
or `jira.env`, validated, composed with `shared-auth.env`, rendered as env/secretRef, then
delivered through the existing secure Bicep secret parameter. Startup verifies and writes
the public CA to `/tmp/researchtrack-kafka/ca.crt`. It rejects private/non-CA/invalid/expired/
future certificates. Base64 is not encryption; secret delivery avoids unnecessary disclosure
and the generated spec remains mode 0600. No production CA/key/password is committed.
CA delivery does not disable certificate or endpoint verification or affect global HTTPS trust.

Environment endpoints/topic/version/group are externalized. Examples are blank while disabled.
Local/Test can supply their own external TLS endpoint/CA; production rejects public/localhost/
internal 29092 listeners. Exact live endpoint/SAN/routing and chosen client mappings still need
verification. The helper has no network client, so even an enabled startup is not connectivity proof.

## Local automated verification

Executed on macOS with GNU Bash 5.3.15 and OpenSSL 3.6.5 on PATH. Early attempts under
macOS system Bash/LibreSSL failed their platform assumptions; reruns under the intended
GNU Bash/OpenSSL toolchain passed. Offline timeout is bounded by subprocess where GNU
timeout is unavailable. VM runtime uses the existing Ubuntu GNU timeout.

`global.json` requests SDK 10.0.300 with `latestFeature`; installed SDK 10.0.400 is compatible.
Database integration categories were excluded because no disposable DB environment was supplied.

| Executed command (repository root) | Actual result |
|---|---|
| `dotnet restore ResearchTrack.sln` | PASS |
| `dotnet build ResearchTrack.sln -c Release --no-restore` | PASS, 0 warnings / 0 errors |
| `dotnet test ResearchTrack.sln -c Release --no-build --filter 'Category!=DatabaseIntegration'` | PASS, 334 tests: GitHub 180, Jira 51, Gateway 24, Auth 15, Project 9, Meeting 27, Submission 28 |
| `python3 deploy/azure/validation/test_kafka_topics_config.py` | PASS, 15 test methods with parameterized/subtest invalid cases |
| `deploy/azure/validation/test-kafka-smoke.sh` | PASS, 9 cases |
| `deploy/azure/validation/test-kafka-operations.sh` | PASS, 43 cases; bundle registry/helper permissions and approval included |
| `deploy/azure/validation/test-network-probe.sh` | PASS, 12 cases |
| `deploy/azure/validation/test-private-ip-check.sh` | PASS, 10 cases |
| `deploy/azure/validation/test-validate-azure-env-files.sh` | PASS, 22 cases including legacy/disabled/enabled/unrelated Kafka checks |
| `deploy/azure/validation/test-service-env-composition.sh` | PASS, 10 composition cases |
| `deploy/azure/validation/test-deploy-image-selection.sh` | PASS, 11 cases |
| `python3 deploy/build/test_service_impact.py` | PASS, 32 tests; new Kafka suite separately checks current linked-source impact |
| `deploy/azure/validation/test-preflight.sh` | PASS, 5 cases |
| `deploy/azure/validation/test-runtime-readiness.sh` | PASS, 4 cases |
| `deploy/azure/validation/test-runtime-power.sh` | PASS, 11 cases |
| `deploy/validate-shared-auth-wiring.sh` | PASS |
| `kafka-topics.sh validate-manifest` | PASS, no broker access |
| Bash syntax and ShellCheck on all modified shell files | PASS |
| Python AST syntax / `actionlint .github/workflows/backend-ci.yml` / `git diff --check` | PASS |

The 17 new .NET cases cover default/disabled behavior, unsafe production settings, actual
env binding, public CA file delivery, repeated initialization, mounted CA, missing trust,
invalid/private material, non-CA/expired/future certificates. The new Python suite covers
manifest failures, unapproved exclusion, default dry run, safe apply/check/no-op/drift/query
failure, secret-reference rendering for both services and migrations, rejected unapproved
rendering, local/Test TLS endpoints, private-material rejection, current image scope and
the real Test VPS validator using synthetic files.

Docker CLI exists, but `docker info` reports no daemon socket. Disposable Kafka, container
execution, broker restart/persistence and real application Kafka integration: **NOT EXECUTED**.
Real application tests are additionally blocked by missing clients/contracts. Azure
connectivity/provisioning/deployment: **NOT EXECUTED** under the task safety rules.
Bicep validation: not required; no Bicep files changed. Local Actions were not triggered.

## Definition of Done

| Requirement | Status | Evidence / outstanding dependency |
|---|---|---|
| 1. Required ResearchTrack topics exist | BLOCKED BY APPLICATION DEVELOPMENT | App contracts/names not supplied; only operational definition approved; tool tested, no live apply/list evidence |
| 2. Producer/consumer connectivity configured | BLOCKED BY APPLICATION DEVELOPMENT | Transport binding/env/trust/rendering ready and tested; actual clients, subscriptions and live network evidence missing |
| 3. Agreed GitHub Kafka flow demonstrated | BLOCKED BY APPLICATION DEVELOPMENT | No approved schema/key/producer/consumer/processing test |
| 4. Agreed Jira Kafka flow demonstrated | BLOCKED BY APPLICATION DEVELOPMENT | No approved schema/key/producer/consumer/processing test |
| 5. Topic ownership/purpose documented | VERIFIED COMPLETE | Current registry/guide document approved operational ownership, proposals, unknown application consumers and exact handoff; add approved mappings when supplied |
| 6. Environment-specific values externalized | VERIFIED COMPLETE | Canonical optional keys, .NET binding, env/renderer tests; no application broker address hardcoded |
| 7. Configuration integrated into deployment | IMPLEMENTED, LIVE VERIFICATION REQUIRED | Existing env/secret renderer and VM bundle integration pass offline; no deployment executed |

## Manual Azure verification after review and separate authorization

On the VM, with the reviewed bundle installed, read-only checks:

```bash
cd /opt/researchtrack
scripts/kafka-topics.sh validate-manifest
scripts/kafka-topics.sh reconcile --dry-run
scripts/kafka-topics.sh reconcile --check
scripts/kafka-topics.sh list
scripts/kafka-topics.sh describe researchtrack.deployment-smoke
scripts/verify-kafka.sh --check
```

Expected: valid registry, candidates excluded, approved definitions match or are reported
missing, partition/RF/retention drift nonzero, no public Kafka binding and verified broker
trust/SAN/storage. Missing/drifting state is a failed acceptance result, not a reason to
delete/recreate topics.

Only after the operator separately authorizes creation against a reviewed approved registry:

```bash
scripts/kafka-topics.sh reconcile --apply
scripts/kafka-topics.sh reconcile --check
```

After developers provide approved contracts and clients, set environment values and review
the generated spec before a separately authorized app deployment. Inspect runtime env names:

```bash
az containerapp show -g "$RESOURCE_GROUP" -n rt-github-prod \
  --query 'properties.template.containers[0].env[].{name:name,secretRef:secretRef}' -o table
az containerapp show -g "$RESOURCE_GROUP" -n rt-jira-prod \
  --query 'properties.template.containers[0].env[].{name:name,secretRef:secretRef}' -o table
az containerapp exec -g "$RESOURCE_GROUP" -n rt-github-prod \
  --command "sh -c 'test -r /tmp/researchtrack-kafka/ca.crt'"
az containerapp exec -g "$RESOURCE_GROUP" -n rt-jira-prod \
  --command "sh -c 'test -r /tmp/researchtrack-kafka/ca.crt'"
```

These CA existence checks apply to enabled revisions after actual deployment, not today's
disabled services. Confirm healthy startup, exact private endpoint, CA fingerprint/SAN,
and library SSL mapping. To prove private network direction, after separate diagnostic-job
and smoke-write authorization run the existing procedure:

```bash
# Required values are supplied by the operator, never copied into source.
export RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM INFRA_PRIVATE_IP
./deploy/azure/scripts/network-probe.sh
```

It creates/runs an Azure diagnostic job and writes only operational smoke data; this command
was not executed here. Do not treat its successful token as either application event flow.
Run developer-provided synthetic GitHub/Jira tests and retain acknowledgement, consumer
receipt and application processing evidence. Their exact commands remain unavailable until
developers implement and identify test entrypoints. For later approved application restarts,
compare topic list/check and consumer recovery before/after, without recreating topics.
Existing Kafka independent restart is optional, explicitly authorized separately, and not
needed to install these local changes.

## Exact local file changes

Modified:

- `.github/workflows/backend-ci.yml`
- `config/env/README.md`
- `config/env/github/.env.example`
- `config/env/jira/.env.example`
- `deploy/azure/scripts/build-vm-bundle.sh`
- `deploy/azure/scripts/install-bundle.sh`
- `deploy/azure/scripts/render-containerapp.py`
- `deploy/azure/validation/test-kafka-operations.sh`
- `deploy/azure/validation/test-service-env-composition.sh`
- `deploy/azure/validation/test-validate-azure-env-files.sh`
- `deploy/azure/validation/validate-azure-env-files.sh`
- `deploy/azure/vm/scripts/kafka-topics.sh`
- `deploy/validate-env-files.sh`
- `docs/devops/azure-production/IMPLEMENTATION_STATUS.md`
- `docs/devops/azure-production/README.md`
- `src/Services/ResearchTrack.GitHubService/Program.cs`
- `src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj`
- `src/Services/ResearchTrack.GitHubService/appsettings.json`
- `src/Services/ResearchTrack.JiraService/Program.cs`
- `src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj`
- `src/Services/ResearchTrack.JiraService/appsettings.json`

Created:

- `deploy/azure/scripts/kafka_config.py`
- `deploy/azure/validation/test_kafka_topics_config.py`
- `deploy/azure/vm/kafka/topics.json`
- `deploy/azure/vm/scripts/kafka-topic-manifest.jq`
- `docs/devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md`
- `docs/devops/azure-production/KAFKA_TOPICS_TASK_REPORT.md`
- `src/BuildingBlocks/Kafka/KafkaRuntimeOptions.cs`
- `tests/ResearchTrack.GitHubService.Tests/KafkaRuntimeOptionsTests.cs`

## Copyable Jira update (not posted)

Prepared local DevOps implementation for review: central approved-topic registry,
non-destructive dry-run/check/explicit-apply reconciliation, bundle validation, optional
GitHub/Jira .NET settings, actual public CA delivery and existing Azure env/secret integration.
Release build and 334 backend tests pass; new offline Kafka/config tests and infrastructure
regressions pass. No commit/push/deployment/live topic write or restart. GitHub/Jira Kafka
application flows remain blocked: approved contracts have been requested and actual
producers/consumers/processing tests are absent. Candidate topics remain unapproved and
settings disabled. Production acceptance awaits contracts, developer clients and separately
authorized live provisioning/connectivity/application evidence. Do not close the full task yet.
