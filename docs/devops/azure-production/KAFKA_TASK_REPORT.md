# Sprint 4 — Deploy and Configure Kafka Infrastructure

Objective: complete Kafka operational verification while preserving the existing Azure deployment.

Current application integration follow-up (2026-10-10): owner-approved same-service
webhook contracts now have real producers/consumers, transactional outboxes and durable
receipts. Defaults remain disabled; isolated real-broker tests and fresh Azure evidence
are pending. This document retains historical infrastructure findings; current acceptance
and command outcomes are in [KAFKA_TOPICS_TASK_REPORT.md](KAFKA_TOPICS_TASK_REPORT.md).

## Initial analysis (2026-10-09)

Inspected latest `origin/main` at `54dcbae67793a2103b6ef78502401bf4ab04b336` before edits. The existing task branch had no commits or changes beyond main. No real environment files or production certificates were read. Azure CLI and a local Docker daemon are unavailable; no live operation is authorized in this session.

| Requirement | Existing implementation | Missing work | Required action |
|---|---|---|---|
| Kafka deployment | Compose Apache Kafka 3.9.1, KRaft, health check, restart policy, limits | Current live evidence | Preserve; add Kafka-only verification |
| Broker configuration | Single broker/controller, replication 1, retention limits | Operational consistency checks | Validate selected properties without printing credentials |
| Listeners and networking | Private-IP SSL 9092, Docker plaintext 29092, localhost controller 9093; restrictive NSG | Host validator passes absent bindings; controller exposure not checked | Inspect Docker bindings and host sockets separately; document effective NSG checks |
| Persistence | Disk mount, persistent data and cluster ID, content hash reconciliation | Missing identity can be regenerated despite existing data | Fail closed on missing/mismatched identity; test guard |
| Service connectivity | Temporary Container Apps Job TCP + verified TLS round trip | Fresh live proof; application messaging code absent | Reuse job, document future client contract |
| Runtime configuration | Protected infra.env; parameterized private IP, retention | Workflow does not pass retention override | Connect existing optional setting to production variable |
| Topic management | Smoke-topic provisioning only | General list/exists/create/describe/config operations | Add minimal non-destructive CLI wrapper |
| Security | TLS server trust, no client auth; restricted key mounts and permissions | Partial CA/password loss silently regenerates material | Fail closed; explicit client hostname verification; document limitations |
| Producer/consumer test | Offset-based, unique token, sync producer, exact comparison | Offset command failure can be ignored; no reusable Kafka-only entry point | Share existing VM smoke implementation; add negative cases |
| Independent restart | Compose can restart one service | Persistence and unrelated-container evidence procedure | Opt-in Kafka-only restart verification using pre-restart message |
| Documentation | Architecture/runbook + historical Azure findings | Dedicated operations guide and acceptance evidence | Add guide/report; label historical evidence accurately |

Architecture drift: the general overview still describes the development foundation; Azure decisions and executable code are the production authority. The VM plan's 8 GiB target differs from the implemented B2s/4 GiB default. IMPLEMENTATION_STATUS mixes an early “never executed” statement with later historical Azure runs. Those historical results and the owner's report that Kafka is deployed are context, not fresh production proof.

## Implementation summary

Reused the Apache Kafka 3.9.1 Compose broker, KRaft setup, private listeners, durable paths, directory mounts, SSL client configuration, offset-based smoke topic, temporary `rt-netcheck-prod` Job and existing workflows. Added no broker, cloud resource, application event contract or authentication migration.

- Shared the existing VM smoke function in `kafka-common.sh`; fail on unsuccessful offset lookup, retain exact token checking and bound CLI steps to 60 seconds. Both the full validator and Kafka-only verifier use it.
- Added `verify-kafka.sh`: default read-only checks, explicit `--smoke` and opt-in `--restart`. Restart verifies old data, a fresh round trip, identity hashes and unaffected VM containers. Azure NSG/ACA checks are reported separately as NOT VERIFIED.
- Added non-destructive topic list/exists/create/describe/config operations; create validates actual partitions, single-broker replication and optional retention without changing an existing topic.
- Fail closed when existing KRaft identity, metadata, CA pair or keystore password is lost/inconsistent; preserve first-deployment generation and established identity. Use exact OpenSSL IP verification and password-file input for PKCS12 export.
- Check desired and active Docker bindings as well as unexpected host sockets, including controller publication. No changes to Compose, listener protocols, private addresses or Bicep/NSG were needed.
- Explicitly retain TLS hostname verification in VM and ACA clients. Expose the existing `KAFKA_RETENTION_HOURS` override through the production workflow, default 72. No production configuration was changed.
- Require all new scripts in deployment bundles and run the offline operations regression suite in backend CI. Preserve OIDC, production environment/approvals, concurrency, destructive What-If protection and application deployment workflows.

## Files modified

| Path | Purpose |
|---|---|
| `.github/workflows/azure-infrastructure.yml` | Pass existing optional retention setting |
| `.github/workflows/backend-ci.yml` | Run new offline operations tests |
| `deploy/azure/scripts/aca-probe.sh` | Explicit client hostname verification |
| `deploy/azure/scripts/install-bundle.sh` | Require Kafka operation helpers in bundles |
| `deploy/azure/scripts/validate-vm-stack.sh` | Use shared smoke; validate Docker bindings/controller exposure |
| `deploy/azure/validation/test-kafka-smoke.sh` | Shared-function tests; topic/offset/deadline failures |
| `deploy/azure/validation/test-network-probe.sh` | Assert hostname verification; reject TLS trust errors |
| `deploy/azure/vm/kafka/client-ssl.properties` | Explicit HTTPS endpoint verification |
| `deploy/azure/vm/scripts/reconcile-stack.sh` | Guard identity/TLS material; exact IP SAN check; password-file export |
| `docs/devops/azure-production/README.md` | Link Kafka guide/report |
| `docs/devops/azure-production/RUNBOOK.md` | Link independent operations; explain retention override |
| `docs/devops/azure-production/IMPLEMENTATION_STATUS.md` | Record current work; distinguish historical/live evidence |

## Files created

| Path | Purpose |
|---|---|
| `deploy/azure/vm/scripts/kafka-common.sh` | Shared smoke and saved-record consumer |
| `deploy/azure/vm/scripts/kafka-identity.sh` | Read-only persistent identity/trust guards |
| `deploy/azure/vm/scripts/kafka-topics.sh` | Safe topic operations |
| `deploy/azure/vm/scripts/verify-kafka.sh` | Check/smoke/approved-restart operator command |
| `deploy/azure/validation/test-kafka-operations.sh` | Synthetic operation, safety and bundle tests |
| `docs/devops/azure-production/KAFKA_OPERATIONS.md` | Junior-friendly operational guide and exact live commands |
| `docs/devops/azure-production/KAFKA_TASK_REPORT.md` | Initial analysis, Jira evidence and copyable comment |

## Automated verification (2026-10-09)

All commands below ran locally. Every Kafka/Azure runtime dependency in regression scripts was mocked; no local Kafka broker, Docker daemon or Azure endpoint was used.

| Command/check | Actual final result |
|---|---|
| `bash deploy/azure/validation/test-kafka-smoke.sh` | PASS, 9 cases; exact/missing/wrong/extra records, producer/admin/deadline errors |
| `bash deploy/azure/validation/test-kafka-operations.sh` | PASS, 43 cases; identity/TLS guards, real certificate SAN checks, readonly mode, bindings/sockets/mounts, restart isolation/persistence, topics and bundle permissions/config |
| `bash deploy/azure/validation/test-network-probe.sh` | PASS, 12 assertions including certificate trust failure and explicit endpoint verification |
| `bash deploy/azure/validation/test-private-ip-check.sh` | PASS, 10 cases |
| `bash deploy/azure/validation/test-preflight.sh` | PASS |
| `bash deploy/azure/validation/test-runtime-readiness.sh` | PASS |
| `bash deploy/azure/validation/test-validate-azure-env-files.sh` | PASS, 18 cases |
| `bash deploy/azure/validation/test-service-env-composition.sh` | PASS, 10 cases |
| `bash deploy/azure/validation/test-deploy-image-selection.sh` | PASS, 11 cases |
| `bash deploy/azure/validation/test-runtime-power.sh` | PASS |
| `bash -n` on every Azure script/VM script/validation script | PASS |
| `shellcheck -S warning` on every changed/new shell script | PASS |
| ShellCheck across all Azure scripts, including unrelated tests | Existing warning: unused `spec` in `test-runtime-power.sh:119`, also present on main; unchanged |
| `INFRA_PRIVATE_IP=10.20.10.4 docker compose --env-file /dev/null -f deploy/azure/vm/compose.yml config --no-env-resolution --format json` | PASS; parsed output confirms only private 9092, version 3.9.1, limits, user, restart and original mounts |
| `actionlint .github/workflows/backend-ci.yml` | PASS |
| `actionlint .github/workflows/azure-infrastructure.yml` | Tool limitation: 1.7.12 rejects existing `concurrency.queue`; same on pristine main. All other checks pass in a temporary copy omitting only that unsupported field |
| `git diff --check` and scoped diff/security review | PASS; no secrets added; Compose/storage/listener/NSG definitions unchanged |
| Bicep build/lint | Local NOT RUN (CLI unavailable); Bicep builds PASS in Backend CI, no Bicep changes |
| Live TLS, ACA communication, persistence/restart | NOT VERIFIED: no Azure operation or broker restart authorized/executed |

The first extended smoke-test attempt exposed macOS's missing GNU `timeout`; the offline fixture now stubs the production deadline contract and tests timeout failure. An initial ad-hoc Compose assertion expected a numeric memory field; Compose v5 serializes it as a string, and converting it before comparison resolved that harness error. Production timeouts and resource settings were not weakened.

The first GitHub Ubuntu run failed the real certificate-prefix regression: its OpenSSL `x509 -checkip` printed a mismatch but returned success, whereas local OpenSSL 3.6.5 returned failure. Replaced that command in reconciliation and the verifier with `openssl verify -verify_ip`, enforcing chain trust and exact SAN verification with an authoritative failure status. Kept the regression test unchanged. Full [Backend CI run 37919376428](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/37919376428) passed on implementation commit `b664ef3`, including all Kafka/related Azure checks, Bicep builds, performance/deployment regressions and .NET restore/build/tests. This report update changes documentation only.

GitHub supports `queue: max`; the current linter does not. The unchanged production setting is preserved: [GitHub Actions concurrency documentation](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency).

## Security, network and restart evidence

Repository evidence confirms a private-IP-only published 9092, Docker-only 29092, localhost controller 9093, restricted host keys, server-authenticated TLS and an NSG that allows only the ACA subnet before denying other private-port sources. Offline failure tests reject public/missing publication, controller/IPv6-public sockets, invalid mounts, TLS errors, lost messages, unrelated restarts and failed recovery. No SASL/mTLS or ACLs were activated; the supplied seven DoD items do not require client identity.

Live Azure tests performed: **none**. Restart performed: **none**. Screenshots: **none**. Historical Azure notes in IMPLEMENTATION_STATUS and the owner's deployed-broker statement are context only. All current production evidence remains pending. The operations guide supplies an additive approved script-staging procedure, read-only effective NSG discovery, VM smoke, existing ACA Job probe and maintenance-window restart commands. It warns that `vm-run.sh` can start a stopped VM and that Run Command invocation success alone is insufficient evidence.

## Definition of Done

| Requirement | Implementation and relevant paths | Verification command / expected success | Actual evidence | Status | Remaining manual action |
|---|---|---|---|---|---|
| Kafka infrastructure deployed | Existing `deploy/azure/vm/compose.yml`, KRaft template; new `vm/scripts/verify-kafka.sh` | On VM `verify-kafka.sh --check`: container running/healthy, API/mounts PASS | Repository and synthetic checks pass; owner reports existing deployment | IMPLEMENTED, NOT LIVE-VERIFIED | Collect current VM check output |
| Required service connectivity available | Existing `scripts/network-probe.sh`, `aca-probe.sh`, NSG | Approved `network-probe.sh`: Kafka TCP/TLS PASS, Job Succeeded | Offline probe suite PASS; .NET Kafka code absent (future integration) | BLOCKED | Azure access + approval for existing ACA Job; clarify separate app-integration ownership if required |
| Network/listener configuration documented | `KAFKA_OPERATIONS.md` A/B/G/K, template, Compose, NSG | Review actual code against documented three listeners/subnets | Direct file comparison and Compose rendering PASS | COMPLETE | Live network evidence belongs to connectivity/exposure items |
| Runtime configuration externalized | `scripts/build-vm-bundle.sh`, workflow, protected infra.env and template | Bundle synthetic override 96 present, modes correct; VM `--check` runtime/config PASS | Synthetic bundle/config checks PASS; actual VM env not read | IMPLEMENTED, NOT LIVE-VERIFIED | Collect selected runtime-setting checks without printing env |
| Basic producer/consumer test passes | Shared `vm/scripts/kafka-common.sh`, validator/verifier, ACA probe | VM `--smoke`: exact unique token via private TLS PASS | 9 mocked smoke cases and operational/probe suites PASS | IMPLEMENTED, NOT LIVE-VERIFIED | Approve smoke writes and retain live output |
| Broker restart succeeds | `vm/scripts/verify-kafka.sh --restart` | Old token survives, fresh token passes, identity unchanged, unrelated containers unchanged | Mocked successful and unsuccessful restart cases PASS | IMPLEMENTED, NOT LIVE-VERIFIED | Explicit maintenance-window approval, run and retain evidence |
| Unnecessary public exposure avoided | Existing Compose + `modules/nsg.bicep`, new host/Docker checks | VM `--check` and effective NIC/subnet NSG review: only private 9092, ACA-only allow, no public internal/controller | Static Compose/NSG review and negative mocks PASS | IMPLEMENTED, NOT LIVE-VERIFIED | Inspect actual effective NSG rules and live bindings |

## GitHub delivery

Draft [PR #77](https://github.com/Nimsara-Jayarathna/researchtrack-backend/pull/77) targets main from `devops/sprint4-kafka-infrastructure-verification`. Code, documentation and the OpenSSL compatibility correction are committed and pushed. PR is unmerged; no deployment workflow was dispatched. Implementation Backend CI passed as linked above; any subsequent documentation-only run is independently visible on the PR.

## Remaining blockers and operator actions

1. Review the draft PR and obtain normal repository approval. Merging infrastructure paths into main will trigger the **existing full infrastructure deployment**; schedule and approve it separately. No merge/deploy was performed here.
2. Obtain Azure access and explicit live-testing authorization. Either use the approved additive staging procedure (no broker restart), or use scripts installed by the next approved normal deployment.
3. Capture VM read-only checks, selected runtime checks, effective NSG review, authorized TLS smoke and the existing ACA Job result. Preserve the actual private IP and redacted logs.
4. Obtain separate restart approval and execute the documented maintenance-window test with deployments/probes paused.
5. Attach those results to Jira; update statuses only from actual evidence. Application event integration and stronger client authentication require separately agreed ownership/acceptance criteria.

## Copyable Jira comment

> Completed the repository work for Sprint 4 Kafka infrastructure verification. Reused the existing Kafka 3.9.1 KRaft deployment, private TLS listener, durable storage and ACA network probe. Added Kafka-only read-only checks, safe topic operations, explicit restart/persistence verification, and guards against replacing lost cluster identity or TLS trust material. Offline Kafka smoke (9), operations (43), network probe (12), and related Azure regression suites pass; changed shell scripts pass Bash/ShellCheck and Compose validation passes. No production deployment, Azure test or Kafka restart was performed. Live VM/NSG/ACA smoke evidence and an explicitly approved maintenance-window restart remain required before closing the task. Guide and acceptance checklist: docs/devops/azure-production/KAFKA_OPERATIONS.md and KAFKA_TASK_REPORT.md.
