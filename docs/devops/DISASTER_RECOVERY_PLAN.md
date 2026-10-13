# ResearchTrack disaster recovery plan

Reviewed 2026-10-12 on `integration/main-kafka-production-20261011`, HEAD
`1810f09535d6a6d00947bcf84d18fde473e40c39`, with local TASK 02–13 changes.
This is a repository audit and a disposable restore drill, not production recovery
certification. No Azure resources, production storage, topics, offsets, credentials,
workflow settings or schedules were changed. Recovery operations below require separate
incident-owner authorization. Main remains the permanent default branch.

## Current capability and principal risk

MySQL has a daily logical backup job retaining seven completed files **on the same
data disk as MySQL, Kafka and monitoring**. No scheduled off-VM upload, Kafka storage
backup, monitoring-data backup or backup vault is defined in the inspected repository.
Externally configured Azure backups/snapshots may exist; their inventory and restore
proof are **NOT VERIFIED**. Local backups alone do not establish off-site DR.

Kafka is **single-node KRaft with replication factor one**. It provides neither
automatic broker failover nor protection against total disk loss. Acknowledgement from
all in-sync replicas still means one broker here. A persistent disk supports ordinary
restart; it does not provide a second recoverable Kafka copy. See
[Kafka KRaft operations](https://kafka.apache.org/39/operations/kraft/).

Production RTO and RPO are **NOT VERIFIED**. Repository recreation cannot restore
lost database rows, expired Kafka records, private CA material or user files.

## Evaluation and evidence

| Requirement | Repository evidence / configured behavior | Severity / missing evidence |
|---|---|---|
| MySQL frequency | `vm/scripts/install-timers.sh`: 02:30 UTC daily, random delay up to 15 min, `Persistent=true` | High: live timer enablement, successful execution and age unknown; powered-off VM cannot run backups |
| Backup location | `vm/scripts/backup-mysql.sh`: `/data/researchtrack/backups` on the workload disk | Critical: loss of this disk loses local backups too |
| Rotation | Seven newest matching completed `.sql.gz` files | High: seven files is not guaranteed seven days; manual runs accelerate rotation |
| Encryption/access | Directory 0700, new files 0600, root-run service; SQL is gzip-compressed, not application-encrypted | High: copy encryption, key escrow, reader permissions and independent retention unknown |
| Off-VM copy | No repository upload/vault policy found | Critical: independent restore source not verified |
| Data disk failure | `modules/managed-disk.bicep`: default 64 GiB StandardSSD_LRS; same disk houses all persistent VM services | Critical: corruption/deletion compromises multiple services and backup copies |
| VM replacement | `modules/vm.bicep`: data disk LUN 0 `Detach`; OS disk `Delete` | High: retain actual disk ID; runtime secrets/certificates on OS disk need a recovery source |
| Cluster identity | Persistent `kafka/cluster-id`, data `meta.properties`; `kafka-identity.sh` checks agreement and refuses partial-history replacement | Critical: identity alone cannot recover missing metadata or records |
| Kafka offsets/topics | Complete Kafka data directory includes metadata, topic logs and internal group offsets | Critical: no independently tested full-storage restore; application topics retain 72 hours |
| Pending outbox | GitHub/Jira DB dumps include `kafka_outbox_messages`, provider deliveries, receipts and sync queues | High: preserve all tables together, exact payloads/IDs/timestamps and encryption keys |
| Duplicate recovery | Inbox receipt/event uniqueness and canonical payload validation; offsets commit after durable handoff | High: guarantees depend on retained receipts and coherent DB/Kafka recovery |
| Monitoring | Persistent Prometheus and Grafana binds; Prometheus default retention 15d | Medium: no scheduled data backup; local monitoring config copies are OS-disk copies only |
| Azure resource recovery | Versioned Bicep/network/ACA/VM definitions | High: effective cloud state, identity, role assignments, DNS and secret escrow unverified |
| Recovery time | No representative full restoration timing or agreed service recovery objective | High: do not use the tiny synthetic drill as production RTO |
| Data loss | Daily logical checkpoint only; no independent binlog archive/PITR process found | Critical: daily checkpoint age applies only to successful active-VM backups; total disk loss may be unrecoverable |

Paths in this table are under `deploy/azure/`. Live deployments may differ from
parameter defaults. Azure managed disks use server-side encryption by default; this
does not encrypt an exported SQL file independently or supply another restore source.
Actual disk encryption properties, export controls and backup RBAC require inspection.
[Azure managed disks overview](https://learn.microsoft.com/en-us/azure/virtual-machines/managed-disks-overview).

`Detach` is a VM deletion policy, not protection against disk/resource-group deletion
or regional failure. No availability zones or application standby are declared in this
VM layout. See [Azure VM deletion behavior](https://learn.microsoft.com/en-us/azure/virtual-machines/delete).

## Recovery inventory

| Material | Host path / recovery source | Preserve and verify |
|---|---|---|
| Six service databases | `/data/researchtrack/mysql` or validated logical dump | Auth, Project, GitHub, Jira, Meeting, Submission; migration histories, authorization, business rows, provider deliveries, outbox, inbox and scheduled jobs |
| Kafka records/metadata | `/data/researchtrack/kafka/data` → `/var/lib/kafka/data` | Entire directory, including `meta.properties`, KRaft metadata, application partitions, `__consumer_offsets` and transaction state; restore compatible Kafka 3.9.1 |
| Kafka cluster ID | `/data/researchtrack/kafka/cluster-id` → read-only `/etc/kafka/cluster-id` | Original cluster ID consistent with recovered metadata and node ID 1 |
| Kafka TLS | `/data/researchtrack/kafka/tls`, including private CA/broker keys, keystore and password | Restricted encrypted recovery copy; original public CA/trust chain, broker SAN and key pairing; never substitute an SSH PEM key |
| Runtime/config | `/opt/researchtrack/runtime`, `/opt/researchtrack/kafka`, Compose/scripts, `/etc/letsencrypt`, systemd units | Restore from authorized secret source and reviewed repository; OS-disk-only copies are vulnerable to VM replacement |
| Prometheus history | `/data/researchtrack/prometheus` → `/prometheus` | Consistent TSDB backup and actual image version/digest |
| Grafana state | `/data/researchtrack/grafana` → `/var/lib/grafana` | Actual database engine, DB/plugins/config and existing encryption secret; repository provisioned dashboards alone omit users/local settings |
| Application release | Reviewed immutable GHCR image digests/source SHA | Seven backend apps, compatible migrations/contracts and authorized runtime configuration |
| Uploaded research files | External object storage used by Submission `S3ObjectStorageService` | Independent object versioning/backup/access verification; MySQL metadata is not a backup of object contents |

Kafka data requires UID/GID 1000:1000; Prometheus 65534:65534; Grafana 472:0,
as configured in `reconcile-stack.sh`. Preserve ownership and restrictive modes from
the recovery manifest. Do not apply recursive permissive chmod/chown to the whole disk.
Verify the five expected broker binds with the existing mount verifier; image-declared
anonymous config volumes do not store the required broker data.

## MySQL backup validation and local correction

The existing dump includes `--single-transaction --routines --triggers --events`, all
six configured databases and `--set-gtid-purged=OFF`. It excludes MySQL system users/
grants, Kafka, TLS, runtime secrets, monitoring history and external objects. Recreate
service accounts from the authorized MySQL runtime configuration using the reviewed
`deploy/mysql/reconcile-databases.sh` (bundled to `/opt/researchtrack/mysql/`), only on an approved recovery target.
Do not assume same-disk MySQL binlogs are an off-VM PITR archive.

An InnoDB logical dump provides a consistent transactional snapshot; concurrent DDL
must be avoided, and nontransactional tables need separate consistency evaluation.
SQL replay can take substantially longer than backup. It does not coordinate a Kafka
checkpoint. [MySQL 8.4 mysqldump documentation](https://dev.mysql.com/doc/refman/8.4/en/mysqldump.html).

Confirmed defects were reproduced before correction: failed/undersized dumps remained
under completed backup names, same-second runs overwrote existing files, and a missing
mount could redirect backups to the OS filesystem. The local correction:

- Refuses to run unless `/data/researchtrack` is a mount point.
- Writes to a private randomly suffixed temporary file on the backup filesystem.
- Publishes by rename only after the dump pipeline, gzip integrity and minimum size pass.
- Removes temporary output on ordinary failure; rotates seven completed copies only
  after successful publication. Existing dump flags, credentials and schedule remain.

The mount check does not establish the correct disk UUID. Gzip/size/hash checks do not
prove SQL restorability, authenticity or completeness. Historical bad completed files
are not repaired or deleted. A hard kill can leave a hidden partial file; inspect it
separately without counting it as a backup. Concurrent manual runs are not a coordinated
checkpoint and should be avoided during scheduled backups. Root DB credentials remain
in the client invocation; a separate approved backup-account/options-file improvement
can reduce privilege/argument exposure without changing this recovery audit.

`Persistent=true` catches up a missed timer on boot, not every missed day's database
state. The unit depends on Docker, not MySQL readiness; catch-up may race startup. No
backup retry, verified off-VM upload or backup-age alert was found. Check the service
result and newest successful checkpoint, especially after VM deallocation.

## Read-only production evidence collection

These commands were **NOT EXECUTED against production**. Run only in an authorized,
already available VM/session; redact resource identifiers as appropriate. Do not print
runtime environment contents, passwords, SQL, tokens, CA keys or full Docker inspect.

```bash
# Operator authenticated to the intended subscription; these are queries only.
RG=rg-researchtrack-prod
VM=vm-researchtrack-infra-prod
az vm show -g "$RG" -n "$VM" --query \
  '{id:id,size:hardwareProfile.vmSize,osDisk:storageProfile.osDisk,dataDisks:storageProfile.dataDisks}' -o json
az disk show -g "$RG" -n disk-researchtrack-data-prod --query \
  '{id:id,size:diskSizeGb,sku:sku.name,managedBy:managedBy,encryption:encryption,networkAccessPolicy:networkAccessPolicy,publicNetworkAccess:publicNetworkAccess}' -o json

# On the existing VM: confirm UUID/source against the retained-disk inventory.
findmnt --target /data/researchtrack --output TARGET,SOURCE,FSTYPE,OPTIONS
lsblk -o NAME,SIZE,FSTYPE,UUID,MOUNTPOINTS
sudo systemctl list-timers --all researchtrack-mysql-backup.timer
sudo systemctl show researchtrack-mysql-backup.service \
  -p Result -p ExecMainStatus -p ExecMainStartTimestamp -p ExecMainExitTimestamp
sudo find /data/researchtrack/backups -maxdepth 1 -type f \
  -name 'researchtrack-mysql-*.sql.gz' -printf '%f %s bytes %TY-%Tm-%TdT%TH:%TM:%TS\n'
# Select a specific listed file; substitute the real filename.
BACKUP=/data/researchtrack/backups/REVIEWED_FILENAME.sql.gz
sudo stat -c '%n %U:%G mode=%a bytes=%s modified=%y' "$BACKUP"
sudo gzip -t "$BACKUP"
sudo sha256sum "$BACKUP"
sudo /opt/researchtrack/scripts/verify-kafka.sh --check
sudo /opt/researchtrack/scripts/kafka-topics.sh reconcile --check
sudo sha256sum /data/researchtrack/kafka/cluster-id \
  /data/researchtrack/kafka/data/meta.properties
```

Expected: intended mounted data disk, successful recent service exit, nonempty completed
gzip, mode 0600 and parent 0700, trusted catalog hash agreement, matching Kafka identity
and existing approved topics. Neither Kafka command requests restart, smoke or topic
creation. Capture group describe output using the existing Kafka runbook; include
group IDs, partition/committed/log-end offsets and lag, never payloads. Query external
vault/snapshot/object-storage inventory and latest independently restorable copy with
the subscription/storage owner; a repository search cannot establish its live absence.

Record actual Docker image digests with the read-only commands in
[tooling maintenance](CICD_TOOLING_MONITORING_MAINTENANCE.md). Keep evidence outside the
VM with UTC timestamp, operator, release SHA, backup hash/size, schema histories, disk
identity, Kafka identity, offsets and restore-test outcome. Do not upload a production
dump or private key to ordinary CI artifacts.

## Database/Kafka checkpoint compatibility

The consumer uses `EnableAutoCommit=false`, `EnableAutoOffsetStore=false` and
`AutoOffsetReset=Earliest`. Missing/out-of-range offsets can replay the earliest
**retained** records; they cannot recover expired/lost records. The durable receipt and
sync handoff precede commit. A commit confirms durable handoff, not necessarily completed
provider synchronization or all business effects.

| Recovery combination | Consequence / required owner investigation |
|---|---|
| Matched DB and Kafka checkpoint | Retained receipts suppress replay after commit uncertainty; pending or expired-lease outbox rows follow normal bounded retry |
| DB newer than recovered Kafka | Retained receipts may suppress older duplicate records. However, PUBLISHED-but-unconsumed events missing from Kafka are **not automatically republished** |
| DB older than recovered Kafka | Newer committed offsets can skip DB work lost during rollback; newer messages can reference missing restored deliveries/outbox rows and hold the consumer |
| Kafka offsets lost | Earliest retained replay is possible; lost receipts undermine deduplication and expired events remain missing |
| Total data disk lost, no independent coherent copies | No guaranteed recovery of business data, event history, offsets, identity or CA material |

Canonical outbox payload/key matching and exact Jira timestamp validation must remain.
Restore complete provider delivery, outbox, receipt, durable sync queue and business
tables together. Include `Jira__TokenEncryptionKey` and equivalent authorized provider
secret material in encrypted escrow; restoring ciphertext without its original key
does not restore usable credentials.

PENDING rows retry when due; PUBLISHING rows recover after lease expiry. Attempts are
bounded at ten; FAILED/held states need reviewed investigation. Preserve attempts,
leases, errors and ordering rather than clearing records to obtain a healthy status.
Any replay of selected PUBLISHED events requires a separately reviewed reconciliation
procedure/tool, source proof and explicit approval; blanket SQL status resets and group
offset resets are not part of this plan. Provider resync can rebuild supported current
state but is not proof of every lost historical event or exactly-once effects.

## Authorized recovery sequence

1. **Declare incident and scope.** Incident owner records failure time, affected disk/VM/
   region, available backup sets, RPO loss estimate and allowable downtime. Obtain explicit
   approval for writer/ingress pause, target provisioning, restore and eventual cutover.
   Preserve surviving volumes/logs and previous healthy revisions; prevent concurrent
   app/infrastructure deployment through existing operational controls.
2. **Choose the surviving-data or backup route.** If the data disk is intact, retain its
   actual Azure resource ID and UUID and attach it to the reviewed replacement VM without
   formatting. If corrupted, preserve it for investigation and restore a validated copy
   to a separate approved target. Never run `mkfs`, Kafka storage format/new cluster UUID,
   topic recreation, schema down migration or offset reset on retained storage.
3. **Select a coherent recovery set.** Verify a trusted manifest, decryption capability,
   six DB schemas/migration histories, full Kafka tree/identity/TLS, secret escrow,
   object-storage state and actual image versions. Independently timed live SQL/log
   copies are not a coordinated recovery set. For future checkpoints, schedule an
   approved ingress/worker/writer quiescence and consistent snapshot/backup procedure;
   no such production operation was implemented or executed here.
4. **Recover Azure dependencies.** Review Bicep What-If against effective inventory;
   retain disk resources and existing identity/OIDC. Restore private networking, intended
   listeners, NSGs, Container Apps environment and approved secret delivery. Confirm the
   expected private IP; if different, review advertised listener, broker SAN/trust and
   app settings before traffic. Do not blindly apply the bootstrap, which can legitimately
   initialize a completely empty disk as a new cluster.
5. **Restore persistence on the isolated candidate.** A physical surviving disk must pass
   filesystem/mount/identity checks before service startup. A logical MySQL dump is
   imported only into a separately approved empty target engine, with no production
   connection and compatible MySQL/image version. Use a protected client option file,
   not printed passwords. Validate tables/rows/precision/business references, routines,
   triggers/events, migration histories and account grants. Restore matched Kafka data/
   identity/TLS with services stopped on the candidate, preserving ownership/modes.
6. **Start candidate dependencies and validate.** Check MySQL/TLS, then Kafka cluster/
   metadata/topics/group offsets using read-only verifiers. Keep application consumers
   fenced until the DB/Kafka recovery boundary is accepted. Review deployment migrations
   before running them; code rollback never implies destructive schema rollback.
7. **Restore monitoring carefully.** Repository dashboards/rules can be reprovisioned;
   Grafana DB/local settings and Prometheus history require their own consistent backup.
   For SQLite Grafana, use an approved stopped-service copy; its actual deployed DB engine
   is unknown. Prometheus snapshots are a state-changing backup operation, not read-only
   diagnostics. If history is deliberately abandoned, obtain owner approval and label the
   new history boundary. Use compatible captured versions rather than pulling `latest`.
8. **Reconcile and validate applications.** Compare restored provider deliveries, receipts,
   sync jobs, outbox states and offsets with the approved loss window. Start compatible
   applications in an isolated/private candidate; prove readiness, existing authorization/
   read paths and selected legitimate test-provider flows. Review failed/held records
   individually. No automatic event injection into production or data repair is authorized.
9. **Approve cutover.** Record expected loss/duplicates and remaining uncertainty. Only the
   incident owner authorizes traffic/DNS/revision changes. Retain original disks/backup
   sets and previous compatible revision for rollback. Fence one side before traffic moves
   to prevent two independent writers. Roll back traffic/code to the retained compatible
   state if validation fails; do not automatically undo migrations or discard new records.
10. **Close with evidence.** Record measured restore/cutover durations, counts and actual
    health/metrics/offset results; verify backup scheduling/freshness and independent copy
    access after recovery. Follow the acceptance procedures in the GitHub/Jira reports.

[Grafana backup guidance](https://grafana.com/docs/grafana/latest/administration/back-up-grafana/)
requires a consistent DB/config/plugin backup; [Prometheus storage guidance](https://prometheus.io/docs/prometheus/latest/storage/)
describes snapshot-based backup and recent-data consistency constraints. Neither repository
provisioning nor TSDB retention is a backup of monitoring history.

## RTO, RPO and cost decisions

With continuously active VM and successful daily dumps, the nominal MySQL checkpoint
age may approach 24 hours plus timer jitter and dump duration. Failure/power-off can make
it older without a useful bound. This is a conditional estimate, not a production RPO
commitment, and says nothing about matching Kafka history. For total disk/region loss
without independently verified copies, recovery time and data loss are unbounded.

Measure RTO as incident decision + target availability + data transfer/import + schema/
identity reconciliation + application verification + traffic cutover. Set separate owner
targets for retained-disk VM replacement and total-disk recovery only after representative
data-volume drills. Current evidence does not justify a numeric production RTO.

| Option, not provisioned | Benefit / cost and limitations | Approval / validation |
|---|---|---|
| Encrypted independent backup copies, successful-backup catalog and restore cadence | Lowest operational scope; storage/transfer/retention costs; choose a failure domain independent of this disk/VM and recovery-key escrow | Owner approves destination, RBAC, key access, retention, tested restore and loss target |
| Managed disk snapshots / Azure Backup policy | Incremental storage/service costs; retained snapshots can protect accidental changes; crash consistency and region/storage independence must be established | Quiescence/restore test and measured recovery-set coherence; snapshot alone is not application HA |
| Independent regional/account copy with versioning or immutability | Better deletion/regional isolation; additional storage, transfer and administration | Data governance, separate recovery access and timed region-loss drill |
| MySQL off-VM binlog archive / validated PITR | Smaller DB loss window; capture/storage/retention complexity; must coordinate Kafka boundary | Separate application/schema/PITR design and full restore test |
| Kafka replication across failure domains / quorum redesign | Automatic broker resilience requires additional brokers/controller capacity and migration effort; greater compute/storage cost | Separate architecture and budget decision; still requires backups for destructive logical changes |

First establish independent restorable copies and an accountable backup-age check; do
not buy standby capacity to conceal the absence of backups. No price, cloud policy,
lock, storage account, vault, replica or production image upgrade was applied here.

## Executed validation and reproducibility

Evidence root: `artifacts/disaster-recovery-task13/` (local, ignored by Git).

| Test | Actual result | What it establishes |
|---|---|---|
| Backup safety tests against original script | FAIL: 4 of 6 methods | Reproduced the four defects, `backup-before-fix.log` |
| Corrected backup safety tests | PASS: 6, zero skipped | Failure/undersize isolation, mounted-disk guard, private valid gzip, rotation, same-second preservation; `backup-tests.log` |
| Real MySQL backup/restore drill | PASS: six synthetic DBs, 6 business rows, 24 outbox rows, 6 receipts | Real dump/import, payload hashes, DATETIME(6), binary bytes, four outbox states, procedures/triggers/events and restored receipt PK duplicate rejection |
| Shell syntax and ShellCheck on both changed shell files | PASS | Two scoped SC2016 annotations explain intentional child-shell variables and SQL identifier backticks |
| Existing Kafka operational safety suite | PASS: shell cases and 8 Python methods | Offline identity/mount/topic/destructive-operation refusal; `kafka-operations.log` |
| GitHub / Jira `KafkaMessagingTests` Release | PASS: 15 / 15, zero skipped | Existing retry, durable handoff and messaging regressions |
| Complete production restore / matched Kafka+MySQL restore / off-VM retrieval | NOT EXECUTED / NOT VERIFIED | Requires approved coherent independent backup and full application drill |

The initial simultaneous GitHub/Jira builds caused a shared MVC test-manifest file
contention: GitHub build FAIL before tests. Jira passed; sequential GitHub rerun passed.
Both logs are retained. This was a local build scheduling issue, not a claimed test pass.

Real fixture: `rt-dr-backup-327260c24c94`, MySQL **8.4.11**, network `none`, no host
ports; two fresh DB volumes and one fresh backup volume. Both containers exited 0,
OOM false after the test; all three synthetic volumes remain for inspection. Actual
dump 87,202 bytes, SHA256
`08a73ef9d0a54d3b4cd52c1db205d62c2df0a594afc135b2635b13267aeca5b7`.
Backup/import each took one second for this tiny fixture, **not production RTO**.
See its `result.json`, source/restored row comparisons and duplicate rejection log.
The schemas are synthetic, not EF/application replay or full historical migration proof.

On macOS with Docker Desktop, Bash, Python 3, OpenSSL and gzip available:

```bash
python3 -m unittest discover -s deploy/azure/validation -p test_backup_mysql.py -v
bash -n deploy/azure/vm/scripts/backup-mysql.sh scripts/test-disaster-recovery-backup.sh
shellcheck deploy/azure/vm/scripts/backup-mysql.sh scripts/test-disaster-recovery-backup.sh
./scripts/test-disaster-recovery-backup.sh
./deploy/azure/validation/test-kafka-operations.sh
dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj \
  -c Release --no-restore --filter FullyQualifiedName~KafkaMessagingTests
dotnet test tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj \
  -c Release --no-restore --filter FullyQualifiedName~KafkaMessagingTests
```

Use a local Unix Docker socket only, allow about 1.5 GiB for the two MySQL containers,
and restore .NET packages first if needed. The drill sources no production env file and
stops only its newly created containers; it intentionally retains fixture volumes and
private temporary credentials. Inspect the emitted fixture location and restrict local
evidence access. Cleanup is a separate deliberate operation on the exact printed
`rt-dr-backup-*` fixture resources; never use global prune or production paths.

TASK 13 changed exactly `backup-mysql.sh`, `test_backup_mysql.py`,
`scripts/test-disaster-recovery-backup.sh` and this plan. Backup fixes remain local and
are **not deployed**. Final acceptance needs current backup freshness, independent copy/
decryption proof, representative full recovery and an approved RTO/RPO decision.
