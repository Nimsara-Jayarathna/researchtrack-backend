# Kafka operations — ResearchTrack Azure Production

Kafka is already deployed according to the team. This guide describes the repository implementation and how to collect fresh evidence. No live Azure check or restart was performed during this work. Run live commands only after the owner approves them. Run restart commands in a maintenance window: the single broker is temporarily unavailable, and clients must retry.

## A. Architecture

The infrastructure VM hosts one `apache/kafka:3.9.1` Docker Compose service, alongside MySQL, Nginx, Prometheus and Grafana. Seven ASP.NET Core applications run in an internal Azure Container Apps environment. Kafka uses KRaft: the same process is broker and controller, with no ZooKeeper. Replication factor 1 means no broker redundancy.

```text
Container Apps subnet (10.20.20.0/23)
    -> infra subnet NSG (authorized subnet only)
    -> VM private address (default 10.20.10.4):9092 / TLS
    -> Kafka container EXTERNAL listener
Docker researchtrack-infra network -> kafka:29092 / plaintext
Kafka process -> localhost:9093 / KRaft controller
```

`docs/architecture/overview.md` describes the earlier development foundation; `DECISIONS.md`, the Azure code, and this guide describe production. `02_INFRASTRUCTURE_VM.md` records an earlier 8 GiB target; the implemented default is B2s/4 GiB. Do not resize the VM as part of Kafka verification.

## B. Configuration

| Setting | Repository/runtime source | Meaning |
|---|---|---|
| Version | `deploy/azure/vm/compose.yml` | Apache Kafka 3.9.1, user 1000:1000 |
| External endpoint | `INFRA_PRIVATE_IP` in `/opt/researchtrack/runtime/infra.env` | IP:9092; only host publication |
| Advertised endpoints | `deploy/azure/vm/kafka/server.properties.template` | EXTERNAL IP:9092; INTERNAL kafka:29092 |
| Controller | Same template | node 1, localhost:9093, broker/controller roles |
| Security map | Same template | EXTERNAL SSL; INTERNAL and CONTROLLER plaintext |
| Client authentication | Same template | `ssl.client.auth=none`; server authentication/encryption only |
| Retention | `KAFKA_RETENTION_HOURS` | Default 72; optional GitHub production variable passed by infrastructure workflow |
| Size limits | Same template | 1 GiB retention per partition; 256 MiB segments; these are not a total-disk limit |
| Default partitions/replication | Same template | 3/1; internal topics replication 1 |
| Broker resources | Compose | 512 MiB JVM heap, 1 GiB container, 0.75 CPU |
| TLS client | `/opt/researchtrack/kafka/client-ssl.properties` | SSL, PEM CA, explicit HTTPS endpoint verification |

`build-vm-bundle.sh` generates infra.env and delivers it as a protected Run Command parameter, installed root:root 0600. Reconciliation renders broker properties root:1000 0640. Do not print either file, `docker compose config` with resolved secrets, or complete container environments into evidence logs.

The private IP is parameterized in Bicep and propagated through deployment outputs. Example only:

```properties
bootstrap.servers=<INFRA_PRIVATE_IP>:9092
security.protocol=SSL
ssl.truststore.type=PEM
ssl.truststore.location=/run/researchtrack/kafka/ca.crt
ssl.endpoint.identification.algorithm=https
```

There are no Confluent/Kafka packages or producer/consumer code in the current .NET services. Do not add arbitrary application env keys and assume they work. When an event contract and client library are approved, the implementation must bind external configuration for bootstrap servers, SSL protocol, a mounted public CA path, and hostname verification (for example the Confluent .NET options `BootstrapServers`, `SecurityProtocol=Ssl`, `SslCaLocation`, `SslEndpointIdentificationAlgorithm=Https`). Never ship CA or broker private keys in an application image. Confirm those options against the selected library version when application integration starts.

## C. Starting Kafka

These are **mutating maintenance commands**, only for an approved start/recovery. Verification does not require reconciliation or deployment:

```bash
sudo /opt/researchtrack/scripts/compose.sh up -d --no-deps kafka
```

Normal infrastructure deployment runs `reconcile-stack.sh`: mounts are already prepared by `configure-vm.sh`, the persistent cluster ID is generated only for an empty first deployment, and `kafka-storage.sh format --ignore-formatted` starts against existing metadata. Reconciliation recreates Kafka if inputs change. It also reconciles other services, so never use it to perform a Kafka-only restart.

Do not run a full-stack `down`, remove volumes, prune data, format disks, or regenerate a cluster ID. A missing/mismatched identity or partial CA/password now stops reconciliation and requires restoring the original material.

## D. Checking health

Run as root on the VM (use Azure Run Command; no public SSH):

```bash
sudo /opt/researchtrack/scripts/verify-kafka.sh --check
sudo /opt/researchtrack/scripts/compose.sh ps kafka
id="$(sudo /opt/researchtrack/scripts/compose.sh ps -q kafka)"
sudo docker inspect --format '{{.State.Running}} {{.State.Health.Status}}' "$id"
sudo /opt/researchtrack/scripts/compose.sh logs --tail 80 kafka
```

`--check` is read-only: validates running/healthy state, restart policy/user, mounts, private Docker publication, unexpected host sockets, KRaft identity, selected properties, permissions, local certificate chain/IP/expiry, live private TLS handshake, internal API and topic listing. No messages, topic creation, restart, CA changes, or cloud writes occur. It prints `PASS`, `FAIL`, `SKIPPED`, or `NOT VERIFIED`. Any failed VM check exits 1; usage errors exit 2. A final VM PASS still leaves NSG and Container Apps checks `NOT VERIFIED`.

Health uses the internal listener and is not proof of external TLS or Azure security. Docker may use NAT without a userspace listening socket, so the verifier checks Docker publication and host sockets separately. Logs may contain operational details; review/redact before attaching them. Never enable shell tracing around credentials.

## E. Topic management

Use `/opt/researchtrack/scripts/kafka-topics.sh` on the VM. Operations use the existing internal listener, not the public network:

```bash
sudo /opt/researchtrack/scripts/kafka-topics.sh list
sudo /opt/researchtrack/scripts/kafka-topics.sh exists researchtrack.deployment-smoke
sudo /opt/researchtrack/scripts/kafka-topics.sh describe researchtrack.deployment-smoke
sudo /opt/researchtrack/scripts/kafka-topics.sh config researchtrack.deployment-smoke
# ONLY after a topic owner approves its name, event contract and capacity:
sudo /opt/researchtrack/scripts/kafka-topics.sh create '<approved-topic>' 3 72
```

`exists` exits 0 when found, 1 when absent or broker query fails. Create uses `--if-not-exists`, replication 1, and checks the actual partition count, replication factor and any requested retention. A mismatch fails without altering an existing topic. There is no delete or alter operation. Omit the last argument to inherit broker retention. The guide creates no production application topics.

Choose names with an owner and event contract; prefer one of dots or underscores to avoid Kafka metric-name collisions. Select partitions for expected parallelism and ordering: ordering is within a partition, and increasing partitions can change key routing. Replication cannot exceed 1 on this broker. Retention must fit expected volume, recovery needs and the shared data disk; broker byte limits apply per partition. Do not partition/alter the smoke topic: it must have 1 partition, replication 1 and 24-hour retention.

## F. Producer/consumer testing

```bash
sudo /opt/researchtrack/scripts/verify-kafka.sh --smoke
```

This first completes the read-only preflight, then reuses `kafka-common.sh`, the VM smoke function used by `validate-vm-stack.sh`. It idempotently creates `researchtrack.deployment-smoke`, records partition 0's end offset, generates a unique token, produces synchronously with acks=all to the real private TLS endpoint and consumes one record at that offset over TLS. Exact token comparison decides success; only CRLF and blank lines are normalized. No application consumer group is used. CLI operations have a 60-second deadline; the consumer waits at most 30 seconds.

Expected: `PASS: TLS producer/consumer (token at offset ...)`, exit 0. Missing/wrong data, admin failures, producer failures or preflight errors exit 1. Old messages cannot satisfy a new token. Run smoke operations sequentially: another writer between offset capture and produce can cause an honest mismatch. Do not run the ACA probe during restart verification. The verifier gives safe diagnostics; the full VM validator retains bounded CLI stderr for troubleshooting.

Offline regression commands, from the checkout:

```bash
bash deploy/azure/validation/test-kafka-smoke.sh
bash deploy/azure/validation/test-kafka-operations.sh
bash deploy/azure/validation/test-network-probe.sh
```

These are mocked logic tests, not proof that Kafka works in Azure.

## G. Network connectivity and Azure commands

The existing NSG allows 9092 only from the Container Apps subnet at priority 200, followed by an explicit deny for other sources at priority 300. Only VM 80/443 are Internet-allowed. Neither controller 9093 nor Docker 29092 is host-published. No SSH rule is added.

Three separate layers must pass: Docker/host bindings, effective Azure NSG, and actual client connectivity. After live testing approval, from a workstation with Azure CLI, Bash, jq, and appropriate access:

```bash
az login
az account set --subscription '<production-subscription-id>'
export RESOURCE_GROUP=rg-researchtrack-prod
export INFRA_VM=vm-researchtrack-infra-prod
export ACA_ENVIRONMENT=cae-researchtrack-prod

# Read-only: discover the actual VM address and NIC, never assume the default.
vm_nic="$(az vm show -g "$RESOURCE_GROUP" -n "$INFRA_VM" --query 'networkProfile.networkInterfaces[0].id' -o tsv)"
export INFRA_PRIVATE_IP="$(az network nic show --ids "$vm_nic" --query 'ipConfigurations[0].privateIPAddress' -o tsv)"
az network nic list-effective-nsg -g "$RESOURCE_GROUP" -n "${vm_nic##*/}" -o json > kafka-effective-nsg.json
az vm get-instance-view -g "$RESOURCE_GROUP" -n "$INFRA_VM" \
  --query "instanceView.statuses[?starts_with(code, 'PowerState/')].code" -o tsv
```

Review all effective allow rules of priority less than 300 for source/destination/port wildcards or port ranges containing 9092, 9093 or 29092. Expected Kafka allow: TCP 9092 from the actual ACA subnet only; deny from all other sources, including the default whole-VNet allow. Review NSGs attached to both NIC and subnet. Save the effective-rule JSON with the evidence; a repository NSG check does not prove the applied rules.

If the VM is stopped/deallocated, stop here and obtain explicit permission to start it. The existing `vm-run.sh` helper automatically starts a stopped VM, so do not treat that helper as a read-only cloud check.

After review approval, stage **only** the operation scripts, the revised non-secret client config, and the shared validator into the already running VM without running reconciliation. The following makes additive file updates only; it neither starts nor restarts containers. Review the approved PR commit first. The root-only runtime broker properties and TLS/data files stay intact:

```bash
stage_script="$(mktemp)"
python3 - "$stage_script" <<'PY'
from pathlib import Path
import base64, shlex, sys
files = [Path('deploy/azure/vm/scripts') / n for n in
         ('kafka-common.sh', 'kafka-identity.sh', 'kafka-topics.sh', 'verify-kafka.sh')]
files += [Path('deploy/azure/vm/kafka/client-ssl.properties'),
          Path('deploy/azure/scripts/validate-vm-stack.sh')]
lines = ['#!/usr/bin/env bash', 'set -euo pipefail', 'umask 077',
         'work="$(mktemp -d)"', 'trap \'rm -rf "$work"\' EXIT']
for file in files:
    dest = '/opt/researchtrack/' + ('kafka/' if file.name.endswith('.properties') else 'scripts/') + file.name
    payload = base64.b64encode(file.read_bytes()).decode()
    mode = '644' if file.name.endswith('.properties') else '755'
    lines += [f"printf '%s' '{payload}' | base64 -d > \"$work/file\"",
              f'install -o root -g root -m {mode} "$work/file" {shlex.quote(dest)}']
lines += ['/opt/researchtrack/scripts/verify-kafka.sh --check']
Path(sys.argv[1]).write_text('\n'.join(lines) + '\n')
PY
az vm run-command invoke -g "$RESOURCE_GROUP" -n "$INFRA_VM" \
  --command-id RunShellScript --scripts "@$stage_script" -o json > kafka-stage-check.json
rm -f "$stage_script"
```

Run Command invocation returning successfully is not sufficient: inspect the returned stdout/stderr, require all VM checks PASS and the final success line. Investigate any FAIL or missing/truncated final output. `NOT VERIFIED` for effective NSG/ACA is expected. This staging procedure does not install the reconciliation safety changes; those enter the next approved normal infrastructure reconciliation. Alternatively, after the ordinary approved deployment has installed all scripts, invoke the existing `/opt/researchtrack/scripts/verify-kafka.sh --check` directly. Do not dispatch the full infrastructure workflow merely for verification; it runs Bicep, OS configuration and the whole stack.

Approve smoke writes before invoking:

```bash
az vm run-command invoke -g "$RESOURCE_GROUP" -n "$INFRA_VM" --command-id RunShellScript \
  --scripts '/opt/researchtrack/scripts/verify-kafka.sh --smoke' -o json > kafka-vm-smoke.json
# Reuse the existing temporary Job; writes the job definition and smoke messages.
export EXPECT_APPS=false
./deploy/azure/scripts/network-probe.sh | tee kafka-aca-probe.log
```

`network-probe.sh` fetches the public Kafka CA via the existing VM helper, applies the existing `rt-netcheck-prod` Job by ARM PUT, starts one execution, and prints its logs. Confirm VM power is running first. It does not deploy application services or restart Kafka. Expected: MySQL TCP PASS, Kafka TCP PASS, Kafka TLS PASS with matching token, execution Succeeded, `RESULT all probes passed`. TLS negotiation, CA trust and endpoint verification occur in Kafka clients, rather than as a separate openssl check. `EXPECT_APPS=false` skips app health requests; set true only when all seven are deployed. This proves the ACA environment can communicate, not that application Kafka integration exists.

## H. Independent restart

This is never part of CI, default verification, the read-only check, or an automatic deployment action added by this task. Obtain owner approval for the restart and allow roughly 3 minutes for health recovery plus smoke/preflight time. Pause deployments and other smoke checks during the maintenance window. An on-VM lock serializes cooperating restart verifiers; it does not lock Azure workflows. Application clients may see temporary failures; single-broker Kafka has no failover.

```bash
# Only in the explicitly approved maintenance window:
sudo /opt/researchtrack/scripts/verify-kafka.sh --restart
# Or, with Azure access and explicit restart approval:
az vm run-command invoke -g "$RESOURCE_GROUP" -n "$INFRA_VM" --command-id RunShellScript \
  --scripts '/opt/researchtrack/scripts/verify-kafka.sh --restart' -o json > kafka-restart.json
```

The script checks Kafka/data/TLS, confirms MySQL/Nginx/Prometheus/Grafana are running, snapshots their container IDs and start times, hashes the cluster ID and metadata, and writes/reads a TLS smoke message. It then runs exactly `compose.sh restart --no-deps kafka`, waits for Kafka health, reads the saved pre-restart token at the same offset, performs a fresh TLS round trip, checks unchanged identity hashes, and confirms all unrelated container IDs remain running with unchanged start times. It never runs `up`, `down`, pulls images or formats storage. The existing Compose has no Kafka dependencies.

Expected: PASS for recovery, saved token, fresh round trip, unchanged metadata, and four unrelated containers. Any failure exits 1. ACA applications are outside this Docker stack; the command cannot restart them. To prove their availability across a window, retain client/HTTP monitoring evidence separately; the script does not claim that proof.

## I. Persistence

The data disk is mounted at `/data/researchtrack` by UUID. Kafka uses `/data/researchtrack/kafka/data` for logs and KRaft `meta.properties`, and `/data/researchtrack/kafka/cluster-id` for the immutable ID. Compose binds these to `/var/lib/kafka/data` and `/etc/kafka/cluster-id`. Data ownership is 1000:1000, directory mode 0750. Container recreation preserves these paths.

Reconciliation now refuses missing/empty identity alongside existing state, mismatched `cluster.id`, or missing metadata in an established deployment. It never “repairs” those cases by generating another ID. Do not delete `.inputs-hash`, metadata or logs to bypass the check. A missing ID must be restored from the original identity after confirming the mounted disk and its matching metadata; escalate rather than copying an unrelated cluster ID.

`configure-vm.sh` formats only a disk with neither a filesystem nor partitions, and reconciliation requires the disk mount before continuing. Neither script is needed for read-only verification. No full backup/restore solution for Kafka is introduced here; the existing backups cover MySQL. Plan disk/identity/CA backups with the infrastructure owner.

## J. Troubleshooting

| Symptom | Investigation and safe next action |
|---|---|
| Container unhealthy | `compose.sh ps kafka`, bounded Kafka logs, internal API check; inspect heap pressure/disk usage |
| Startup failure | Check mounted disk, metadata/cluster ID, runtime file readability; do not format/recreate identity |
| Connection refused | Check running state, Docker private-IP publication and private address; internal health alone is insufficient |
| Connection timeout | Check effective NIC/subnet NSGs, actual ACA subnet/routes, and TCP probe; never add a public allow rule |
| Incorrect advertised listener | Verify selected advertised setting matches actual NIC IP; clients must receive that reachable private endpoint |
| TLS error | Check CA trust, private-IP SAN, expiry and client hostname verification; never disable endpoint verification |
| File permissions | Broker UID/GID 1000 needs runtime 0640/root:1000 and keystore 0440/root:1000; inspect directory traversal/mounts |
| Topic not found | Run list/exists/describe; provision only an owner-approved topic, or run the explicitly authorized smoke setup |
| Producer timeout | Examine network/broker availability and TLS; bound retry, preserve acks=all and exact message comparison |
| Consumer gets no/wrong message | Check producer completion, partition 0 offset, single-partition smoke topic, concurrent writers; rerun sequentially |
| Unavailable after restart | Check startup logs/disk/config, preserve identity and data, stop deployment changes; escalate rather than reset |
| Disk warning | `df -h / /data/researchtrack`, `du -sh /data/researchtrack/kafka/data`; review partition retention/capacity, never manually delete segments |

Diagnostics report selected settings and status, never full infra.env, keystores or private keys. For CLI errors, the smoke function keeps bounded stderr in a temporary file; the full VM validator prints it. Review logs before sharing. The Kafka-only verifier gives step-level results without printing its temporary CLI log.

## K. Security and renewal

EXTERNAL uses TLS 1.2/1.3 with server certificate trust and hostname/IP verification. It does **not** authenticate clients. Authorized network clients can read/write topics: there is no SASL, mTLS or per-topic ACL policy. The supplied Definition of Done does not require client authentication, so this task preserves compatibility and records that limitation.

INTERNAL is plaintext on the Docker network and is reachable by containers attached to it. CONTROLLER is localhost-only within Kafka. NSG restrictions and Docker isolation are required alongside TLS; never publish 29092/9093 or use a public advertised IP. Private keys and passwords remain host-restricted. Kafka receives only its protected broker keystore, public CA and protected server config; application clients need only public trust material.

Reconciliation renews the broker certificate if expiry is within 30 days or the private IP changes, signed by the existing CA. Renewal is tied to **an approved reconciliation**, not an independent certificate timer. A read-only expiry warning gives time to schedule it. Changed certificate/config inputs cause Kafka recreation during the ordinary workflow, so plan downtime and approval. A CA expires after 10 years; replacement requires a coordinated trust migration. Partial/lost CA or keystore-password state now fails closed. Never replace the CA automatically to fix a client trust error.

If client identity becomes an acceptance requirement: confirm event/topic ownership, choose SASL_SSL or mTLS, define secret delivery/rotation and ACLs, prepare .NET and probe-client changes, and test on a parallel private migration listener with a separately approved private binding/NSG restriction. Distribute dual trust/credentials first, move and verify clients, then retire unauthenticated access. Do not activate that proposal in this deployment or assume TLS implies client authentication.

Exact certificate identity checks use `openssl verify -verify_ip`: see [OpenSSL verification options](https://docs.openssl.org/3.0/man1/openssl-verification-options/).

Reference: [Kafka 3.9 admin client configuration](https://kafka.apache.org/39/configuration/admin-configs/) and [KRaft operations](https://kafka.apache.org/39/operations/kraft/).

## L. Verification evidence

Retain approved commit SHA, client time, VM name/private address, redacted VM check output, topic describe/config, TLS smoke result, effective NSG JSON, ACA execution name and probe logs, and restart before/after assertions. Record failures and skipped checks too. Run Command output is limited; missing final results are `NOT VERIFIED`, not success. No fabricated logs or screenshots should be attached.

| Evidence | Success condition |
|---|---|
| VM `--check` | All VM checks PASS, no FAIL, exit 0; separate Azure checks explicitly unresolved |
| Topic describe | Smoke PartitionCount 1, ReplicationFactor 1 |
| VM `--smoke` | Unique token round trip over private TLS PASS |
| Effective NSG review | Only authorized ACA subnet allowed 9092; other sources denied; no 9093/29092/SSH public allow |
| ACA probe | TCP and Kafka TLS PASS, execution Succeeded, overall result passed |
| `--restart` | Old token survives, new token passes, hashes unchanged, unrelated IDs/start times unchanged |

See `KAFKA_TASK_REPORT.md` for the initial analysis, automated test evidence and Jira acceptance statuses.
