# Kafka infrastructure, TLS and persistence verification — TASK 05

Story DO-401 / SCRUM-201. Inspected and tested locally on 2026-10-12 in
`integration/main-kafka-production-20261011`, based on commit
`1810f09535d6a6d00947bcf84d18fde473e40c39`, retaining earlier local audit changes.
Repository configuration is consistent with the existing single-broker deployment.
One verifier false-positive was reproduced and corrected. No live Azure/Kafka access,
deployment, restart, storage repair, topic/offset change, commit or push occurred.

## Configuration evidence and limits

CONFIG VERIFIED below means inspected source, synthetic Compose output or isolated tests;
it does **not** mean the current production resource was inspected.

| Requested control | Configuration evidence | Live evidence still required |
|---|---|---|
| 1. Kafka 3.9.1 KRaft | `compose.yml`: `apache/kafka:3.9.1`; broker/controller roles, node 1, static quorum `1@localhost:9093`; no ZooKeeper | Running image digest and CLI version |
| 2. Health/startup | Format uses the persisted cluster ID and `--ignore-formatted`, followed by `exec` broker start with explicit rendered properties. Health uses `localhost:29092`, interval/timeout 15s, retries 10, start period 60s | Running/healthy state and startup outcome |
| 3. Private listener | Docker publishes **only** `${INFRA_PRIVATE_IP}:9092`; container EXTERNAL binds 0.0.0.0 **inside its namespace** | Actual Docker publication and host sockets |
| 4. TLS/trust | EXTERNAL SSL; TLS 1.2/1.3; PKCS12 broker key; PEM CA client trust; HTTPS endpoint verification. Broker certificate SAN contains the private IP | Current chain, SAN, expiry, mounted trust and live handshake |
| 5. Advertisement | `EXTERNAL://<private-IP>:9092,INTERNAL://kafka:29092`; controller stays loopback 9093 | Deployed properties and returned client metadata |
| 6. Persistent storage | `log.dirs=/var/lib/kafka/data` binds `/data/researchtrack/kafka/data`, on the managed data disk | Actual filesystem/device/space and mounts |
| 7. Cluster identity | `/data/researchtrack/kafka/cluster-id` is generated only for an empty first deployment, mounted read-only; identity guard compares `data/meta.properties` and refuses lost/mismatched established state | Existing ID matches metadata; original backups available |
| 8. Docker paths | Five exact binds; only two unused image-created anonymous volumes admitted. Full mount predicate unchanged by TASK 05 | Current Docker/image mount diagnostic |
| 9. Restart behavior | `unless-stopped`; explicit Kafka-only maintenance restart checks identity, saved message and unrelated container start times; preflight blocks unsafe restart | Approved maintenance evidence, **not executed here** |
| 10. Resources | 512 MiB JVM heap, 1 GiB container memory, 0.75 CPU; verified normalized Compose output | Actual HostConfig, OOM history, CPU/memory/disk headroom |
| 11. Permissions | UID/GID 1000; data 1000:1000/0750, runtime properties root:1000/0640, mounted PKCS12 root:1000/0440; private CA key/password root:root/0600 | Actual host modes and in-container readability |
| 12. Topics/retention | Approved registry below; manifest and non-destructive topic drift tests pass; default broker retention 72h, 1 GiB/partition, 256 MiB segments | Existing partitions, replication and effective topic retention |
| 13. Exposure | 29092 is Docker-network-only; 9093 is container loopback. NSG allows TCP 9092 from ACA subnet, then denies other sources | Effective NIC/subnet rules and routes; configuration alone is insufficient |
| 14. ACA connectivity | Internal ACA environment/VNet, default ACA subnet `10.20.20.0/23`, VM private IP `10.20.10.4`; application clients enforce SSL and endpoint verification | Existing job/application evidence from the actual ACA environment |
| 15. Exporter | Digest-pinned v1.10.0, `kafka:29092`, matching Kafka version, no host port, no secrets/socket mounts, no automatic topic creation; Prometheus targets `kafka-exporter:9308` | Live scrape success, one broker and expected topic metrics |

Sources: [Compose](../../deploy/azure/vm/compose.yml),
[broker template](../../deploy/azure/vm/kafka/server.properties.template),
[TLS client](../../deploy/azure/vm/kafka/client-ssl.properties),
[reconciliation](../../deploy/azure/vm/scripts/reconcile-stack.sh),
[identity guard](../../deploy/azure/vm/scripts/kafka-identity.sh),
[NSG](../../deploy/azure/modules/nsg.bicep), and
[production parameters](../../deploy/azure/parameters/production.bicepparam).
The configured combined/static KRaft topology is supported by Kafka 3.9;
one broker/controller and replication factor 1 provide no broker redundancy.
[Apache Kafka KRaft configuration](https://kafka.apache.org/39/operations/kraft/)

The Kafka CA is the existing X.509 CA certificate, **never an SSH private key**. The CA
private key remains outside the container; only the broker PKCS12 and public CA enter its
restricted TLS directory. Container Apps receive the validated public CA through secretRef
delivery and materialize a verified file; application defaults stay disabled until configured.
Production enablement and secret contents were not inspected or changed here.
`ssl.client.auth=none` means server-authenticated encryption without client mTLS/SASL/ACLs;
retain the existing private-network boundary rather than changing client authentication in this task.

`unless-stopped` restarts an exited process, not a container merely marked unhealthy.
Kafka CLI JVMs add memory usage beyond the 512 MiB broker heap. The single broker shares
the default 4 GiB VM and 64 GiB disk with other services; configured limits do not prove
capacity. Retention limits are per partition, with segment-based cleanup, not a total-disk cap.
The Kafka version tag is fixed but not digest-pinned; capture production's actual image identity.

## Previous mount failure

The cached local Linux/arm64 `apache/kafka:3.9.1` image declares:

```text
/etc/kafka/secrets
/var/lib/kafka/data
/mnt/shared/config
```

This matches the [Apache 3.9.1 JVM Dockerfile](https://github.com/apache/kafka/blob/3.9.1/docker/jvm/Dockerfile).
The explicit data bind replaces the image's `/var/lib/kafka/data` volume. Docker can still
create anonymous volumes for the other two unused destinations, yielding seven mounts.
The prior exact-five-mount assumption could therefore reject an otherwise correct layout.
This remains a **plausible explanation**, not a proven diagnosis of the historical live failure.

The existing corrected predicate requires:

| Host source | Container destination | Access |
|---|---|---|
| `/data/researchtrack/kafka/data` | `/var/lib/kafka/data` | read/write |
| `/data/researchtrack/kafka/cluster-id` | `/etc/kafka/cluster-id` | read-only |
| `/opt/researchtrack/kafka` | `/etc/researchtrack/kafka` | read-only |
| `/opt/researchtrack/runtime/kafka` | `/etc/researchtrack/kafka-runtime` | read-only |
| `/data/researchtrack/kafka/tls/container` | `/etc/researchtrack/kafka-tls` | read-only |

It admits optional volumes **only** at `/etc/kafka/secrets` and `/mnt/shared/config`, of
type volume, read/write, with 64-character lowercase hex anonymous-style names. Mount
destinations must be unique, with five to seven total mounts. Missing/wrong binds, writable
configuration, extra host binds, named image volumes, duplicates and nested shadow mounts
all remain rejected. The predicate was compared with HEAD and is **identical**; it was not
loosened to achieve PASS. A 64-hex name is a narrow naming check, not independent proof of
a volume's provenance; the live image/mount diagnostic must establish the full context.
No anonymous volume holds configured Kafka log data, KRaft identity or active broker/TLS configuration.

If the diagnostic matches this layout but an older verifier rejects it, the narrow correction
is the already-reviewed verifier logic, subject to a separate approved script update.
If sources/destinations or filesystem identity differ, stop and obtain a storage-specific
review. Do not recreate the broker, redirect its datadir, delete volumes or generate a new ID.

## Confirmed defect and local fix

The previous property checks used `grep -Fxq` to find an expected line. A later override
could change the effective advertised address or clear
`ssl.endpoint.identification.algorithm` while the verifier still reported PASS. Java
Properties also permits alternate separators and escaped keys.
[Java Properties format](https://docs.oracle.com/en/java/javase/21/docs/api/java.base/java/util/Properties.html#load(java.io.Reader))

Four synthetic adversarial files reproduced false PASS for both `--check` and `--restart`;
the stub restart was wrongly reached. No real broker was used. The initial failing output
is retained in `artifacts/kafka-infrastructure-verification/properties-regression-before-fix.log`.

[verify-kafka.sh](../../deploy/azure/vm/scripts/verify-kafka.sh) now checks the repository's
generated single-line `key=value` format, rejecting duplicate keys, alternate/escaped-key
syntax and continuation lines without printing values. Expected settings must still match
exactly. It deliberately does not attempt to interpret every Java Properties syntax:
noncanonical manual edits fail verification and require review. Generated broker/client
configuration, TLS values and startup behavior remain unchanged.

The existing operational fixture gained four cases, each run in check/smoke/restart modes
(**12 additional scenarios**): duplicate advertised listener; duplicate empty hostname
verification; whitespace/colon override; Unicode-escaped override. They verify nonzero
exit, no smoke production, no restart, and no credential output. Existing mount, identity,
TLS SAN, persistence and topic safety cases remain in the same suite.

The monitoring suite initially failed one stale assertion requiring the VPS production
route removed locally by TASK 03. Its test now asserts the replacement unprivileged
Test-only target gate, build/deploy success dependencies and static Test Environment,
while retaining integration Azure branch and develop Test assertions. No workflow was changed by TASK 05.

## Actual test results

| Executed validation | Result |
|---|---|
| `test-kafka-operations.sh` | **PASS: 62 operational cases**, plus embedded 8 disposable-broker-control/metrics tests |
| `test-kafka-smoke.sh` | **PASS: 9 mocked exact-token/logging/deadline cases** |
| `test_kafka_topics_config.py` | **PASS: 21 tests**, including public CA delivery/configuration and non-destructive manifest/topic behavior |
| `test-network-probe.sh` | **PASS: 12 checks**, using mocked Azure/CLI behavior |
| `test-private-ip-check.sh` | **PASS: 10 cases** |
| `test_monitoring_reconciliation.py` | **PASS: 12 tests after stale assertion correction** |
| `test_workflow_release_gates.py` | **PASS: 31 tests** |
| `scripts/test-kafka-observability.sh` | **PASS: 159 configuration assertions/checks, 30 Prometheus scenarios, 55 dashboard expressions** |
| Additional synthetic Compose audit | **PASS:** exact version, user, private publication, five binds, heap/memory/CPU and exporter isolation |
| Six inspected/changed shell files, `bash -n` | **PASS** |
| ShellCheck at warning/error severity | **PASS** |
| Raw ShellCheck | **FAIL:** existing informational SC1091 source-resolution, SC2016 intentional jq/container-shell quoting, SC2015 conditional-expression advisories |
| Live VM health/TLS/mounts/NSG, actual ACA/exporter connectivity, production restart/persistence | **NOT EXECUTED** |
| Full .NET solution and Docker Kafka/MySQL end-to-end recovery suite | **NOT EXECUTED in TASK 05**; no application/broker configuration change requiring a repeat here |

The operational suite uses disposable files, stub Docker/Kafka commands, a real temporary
OpenSSL certificate/SAN fixture and a loopback test-only metrics server. Its restart cases
prove script safety and decision logic, not real broker crash recovery. Promtool runs against
temporary monitoring configuration in an isolated Docker container. No test connects to Azure.
Fresh logs, final `results.json`, source hashes, synthetic Compose output and cached image
metadata are retained in the ignored `artifacts/kafka-infrastructure-verification/` directory.
The cached image digest is local evidence only; it is not a production digest claim.

Reproduce from the repository root on macOS with Docker Desktop running, Bash, Python 3,
jq and OpenSSL available:

```bash
bash deploy/azure/validation/test-kafka-operations.sh
bash deploy/azure/validation/test-kafka-smoke.sh
python3 deploy/azure/validation/test_kafka_topics_config.py
bash deploy/azure/validation/test-network-probe.sh
bash deploy/azure/validation/test-private-ip-check.sh
python3 deploy/azure/validation/test_monitoring_reconciliation.py
python3 deploy/azure/validation/test_workflow_release_gates.py
./scripts/test-kafka-observability.sh
```

## Read-only production operator procedure

These instructions were **prepared, not executed**. Use an already approved Azure context
and existing VM administration session (Azure Portal Run Command is the established path;
do not add public SSH). First verify the subscription and VM power state. If stopped or
deallocated, stop here; this task does not authorize waking production. In particular,
`vm-run.sh` can automatically start a stopped VM and must not be used as a read-only shortcut.

With existing Azure CLI read access, collect control-plane evidence only:

```bash
az account show --query '{subscription:id,tenant:tenantId}' --output json
az vm get-instance-view --resource-group rg-researchtrack-prod \
  --name vm-researchtrack-infra-prod \
  --query "instanceView.statuses[?starts_with(code, 'PowerState/')].code" --output tsv
vm_nic="$(az vm show --resource-group rg-researchtrack-prod \
  --name vm-researchtrack-infra-prod --query 'networkProfile.networkInterfaces[0].id' --output tsv)"
az network nic show --ids "$vm_nic" --query 'ipConfigurations[].privateIPAddress' --output json
az network nic list-effective-nsg --resource-group rg-researchtrack-prod \
  --name "${vm_nic##*/}" --output json
```

Expected private address is the actual configured value, default `10.20.10.4`. Review NIC
and subnet effective rules, including wildcards/ranges and higher-priority overrides.
Expected Kafka allow is TCP 9092 from the actual ACA subnet only; deny every other source
for 9092/9093/29092. A private binding alone does not prove Azure denies Internet access.

On the **already running** VM, as root or through the existing root Run Command context:

```bash
sudo /opt/researchtrack/scripts/verify-kafka.sh --check
sudo /opt/researchtrack/scripts/diagnose-kafka-mounts.sh
kafka_container_id="$(sudo /opt/researchtrack/scripts/compose.sh ps -q kafka)"
sudo docker inspect --format \
  'image={{.Config.Image}} image_id={{.Image}} running={{.State.Running}} oom={{.State.OOMKilled}} restarts={{.RestartCount}} memory={{.HostConfig.Memory}} nano_cpus={{.HostConfig.NanoCpus}}' \
  "$kafka_container_id"
sudo /opt/researchtrack/scripts/compose.sh exec -T \
  -e KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties \
  -e KAFKA_HEAP_OPTS=-Xmx256m kafka /opt/kafka/bin/kafka-topics.sh --version
sudo /opt/researchtrack/scripts/kafka-topics.sh reconcile --check
sudo openssl x509 -in /data/researchtrack/kafka/tls/broker.crt \
  -noout -subject -issuer -dates -fingerprint -sha256 -ext subjectAltName
sudo sha256sum /data/researchtrack/kafka/tls/ca.crt \
  /data/researchtrack/kafka/tls/container/ca.crt
sudo df -h /data/researchtrack
sudo sha256sum /opt/researchtrack/scripts/verify-kafka.sh
```

Expected verifier exit 0, all VM checks PASS, smoke/restart SKIPPED, and explicit NSG/ACA
**NOT VERIFIED** notices. Expected image/version 3.9.1, memory 1073741824, NanoCPUs 750000000,
no OOM, correct mount table and mounted data filesystem. Compare the deployed script hash
with the approved source; local hardening is **not** automatically present on the VM.
A missing broker/disk, expired certificate, lost identity or any FAIL requires diagnosis,
not starting/reconciling/formatting/recreating resources to manufacture a PASS.
The two public CA file hashes must agree. Record certificate SAN/validity without reading
the PKCS12 or private keys. `--check` tests the certificate chain/IP and a 30-day expiry margin.

For a read-only Kafka protocol/metadata check over the same external TLS listener, set the
address to the actual private IP collected above, then run on the VM:

```bash
kafka_private_ip='10.20.10.4' # Replace only if the actual approved address differs.
sudo timeout 60 /opt/researchtrack/scripts/compose.sh exec -T \
  -e KAFKA_LOG4J_OPTS=-Dlog4j.configuration=file:/opt/kafka/config/tools-log4j.properties \
  -e KAFKA_HEAP_OPTS=-Xmx256m kafka /opt/kafka/bin/kafka-broker-api-versions.sh \
  --bootstrap-server "$kafka_private_ip:9092" \
  --command-config /etc/researchtrack/kafka/client-ssl.properties
```

Expected broker node 1 reachable at that private IP:9092 with API versions and exit 0.
This reads metadata, not records, and uses the mounted CA plus endpoint verification.
It proves VM-originated protocol access; ACA-originated connectivity still needs its own evidence.

Read-only topic verification should report MATCH for:

| Approved topic | Partitions / replication | Effective `retention.ms` | Consumer group |
|---|---|---|---|
| `researchtrack.deployment-smoke` | 1 / 1 | 86400000 (24h) | none |
| `researchtrack.github.events.v1` | 1 / 1 | 259200000 (72h) | `researchtrack-github-webhook-v1` |
| `researchtrack.jira.events.v1` | 1 / 1 | 259200000 (72h) | `researchtrack-jira-webhook-v1` |

`reconcile --check` exits nonzero on missing topics or drift; it does not create, alter or
delete topics. Do not replace it with `--apply` or reset consumer groups to fix evidence.

For exporter/Prometheus evidence, on the same VM use bounded loopback **GET** queries:

```bash
curl -fsS --max-time 10 --get \
  --data-urlencode 'query=up{job="researchtrack-kafka"}' \
  http://127.0.0.1:9090/api/v1/query | jq .
curl -fsS --max-time 10 --get --data-urlencode 'query=kafka_brokers' \
  http://127.0.0.1:9090/api/v1/query | jq .
```

Expected fresh sample value 1 for the Kafka scrape and one broker; inspect result timestamps
and topic/group metrics against the registry. Empty/stale results are missing evidence,
not PASS. Neither loopback query proves the ACA-to-Kafka direction.

Use existing approved application/network-probe evidence from the actual ACA environment
to establish that direction. **Do not run `network-probe.sh` as a read-only diagnostic:**
it applies/starts an ACA job and produces a smoke message. Fresh probe, `verify-kafka.sh
--smoke`, and `--restart` need a separate approved operation/maintenance window and are not
part of these commands. Capture approved evidence of verified CA/IP, metadata reachability,
processing, offsets and persistence without reading message payloads into this report.

Record UTC timestamp, source SHA/script hash, actual image ID/version, exit codes, sanitized
mount/filesystem and TLS results, effective NSG review, topic retention and fresh monitoring
samples. Never attach complete runtime env files, rendered broker passwords, keystore/CA
private keys, full container environments or application messages. If the historical mount
failure remains, retain `diagnose-kafka-mounts.sh` output before proposing a targeted correction.

## Local review scope

- `deploy/azure/vm/scripts/verify-kafka.sh`: reject overridden/ambiguous generated properties.
- `deploy/azure/validation/test-kafka-operations.sh`: extend the existing fixture and assert no smoke writes after failed preflight.
- `deploy/azure/validation/test_monitoring_reconciliation.py`: align stale VPS authorization assertion with TASK 03.
- `docs/devops/azure-production/KAFKA_OPERATIONS.md`: link this fresh verification and property diagnostics.
- This report. Broker/Compose/TLS/topic settings, credentials, workflows and all persistent data remain untouched by TASK 05.

If the verifier change needs to be withdrawn locally, revert only its reviewed TASK 05 hunk
and associated tests, preserving prior tasks; do not discard the whole worktree. An older
verifier can miss overridden properties. Any future deployed-script rollback must be separately
approved and must not reset broker identity, TLS trust, topics, offsets, migrations or data.
