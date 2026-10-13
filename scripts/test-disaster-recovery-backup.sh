#!/usr/bin/env bash
# Real logical backup/restore on two new network-isolated MySQL containers.
# No production environment files, broker, published ports or existing volumes.
set -Eeuo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
for binary in docker python3 openssl gzip; do
  command -v "$binary" >/dev/null || { echo "NOT EXECUTED: missing $binary" >&2; exit 2; }
done
docker info >/dev/null 2>&1 || { echo 'NOT EXECUTED: local Docker Desktop required.' >&2; exit 2; }
[[ -z "${DOCKER_HOST:-}" || "$DOCKER_HOST" == unix://* ]] || exit 2
[[ "$(docker context inspect --format '{{.Endpoints.docker.Host}}')" == unix://* ]] || exit 2
umask 077
work="$(mktemp -d "${TMPDIR:-/tmp}/researchtrack-dr.XXXXXX")"
project="rt-dr-backup-$(openssl rand -hex 6)"
evidence="$root/artifacts/disaster-recovery-task13/$project"
mkdir -p "$evidence"
source_id=""; target_id=""
cleanup() {
  # Keep all fixture storage for inspection; only stop containers created here.
  for id in "$source_id" "$target_id"; do
    [[ -z "$id" ]] || docker stop --time 30 "$id" >/dev/null 2>&1 || true
  done
  printf 'project=%s\nsource=%s\ntarget=%s\nprivate_work=%s\n' "$project" "$source_id" "$target_id" "$work" > "$evidence/fixture-location.txt"
  echo "RETAINED: $project; evidence=$evidence; three fixture volumes preserved."
}
trap cleanup EXIT
password="$(openssl rand -hex 24)"
printf 'MYSQL_ROOT_PASSWORD=%s\n' "$password" > "$work/mysql.env"
for service in auth project github jira meeting submission; do
  key="$(printf '%s' "$service" | tr '[:lower:]' '[:upper:]')"
  printf '%s_DB_NAME=researchtrack_dr_%s\n' "$key" "$service" >> "$work/mysql.env"
done
cat > "$work/compose.sh" <<'SH'
#!/usr/bin/env bash
set -euo pipefail
[[ "${1:-} ${2:-} ${3:-}" == 'exec -T mysql' ]] || exit 2
shift 3
exec "$@"
SH
# The MySQL utility image lacks mountpoint; check its actual mount namespace.
cat > "$work/mountpoint" <<'SH'
#!/usr/bin/env sh
[ "$1" = -q ] || exit 2
awk -v path="$2" '$5 == path {found=1} END {exit !found}' /proc/self/mountinfo
SH
chmod 755 "$work/compose.sh" "$work/mountpoint"
source_id="$(docker run -d --name "$project-source" --network none --label researchtrack.fixture=dr-backup \
  --env-file "$work/mysql.env" --memory 768m --cpus 1 \
  --mount "type=volume,src=$project-source-mysql,dst=/var/lib/mysql" \
  --mount "type=volume,src=$project-backups,dst=/data/researchtrack" \
  --mount "type=bind,src=$root/deploy/azure/vm/scripts/backup-mysql.sh,dst=/opt/researchtrack/scripts/backup-mysql.sh,readonly" \
  --mount "type=bind,src=$work/compose.sh,dst=/opt/researchtrack/scripts/compose.sh,readonly" \
  --mount "type=bind,src=$work/mountpoint,dst=/usr/local/bin/mountpoint,readonly" \
  mysql:8.4 --innodb-buffer-pool-size=67108864)"
target_id="$(docker run -d --name "$project-target" --network none --label researchtrack.fixture=dr-backup \
  --env-file "$work/mysql.env" --memory 768m --cpus 1 \
  --mount "type=volume,src=$project-target-mysql,dst=/var/lib/mysql" \
  mysql:8.4 --innodb-buffer-pool-size=67108864)"
for id in "$source_id" "$target_id"; do
  CID="$id" python3 - <<'PY'
import json,os,subprocess
c=json.loads(subprocess.check_output(['docker','inspect',os.environ['CID']]))[0]
assert c['Name'].startswith('/rt-dr-backup-')
assert c['Config']['Labels']['researchtrack.fixture']=='dr-backup'
assert c['HostConfig']['NetworkMode']=='none' and not c['HostConfig']['PortBindings']
for m in c['Mounts']:
    if m['Type']=='volume': assert m['Name'].startswith('rt-dr-backup-')
PY
  for ((attempt=0;attempt<90;attempt++)); do
    if docker exec "$id" sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql --protocol=TCP -h 127.0.0.1 -uroot -Nse "SELECT 1"' >/dev/null 2>&1; then break; fi
    [[ "$attempt" != 89 ]] || { echo 'FAIL: isolated MySQL readiness timeout.' >&2; exit 1; }
    sleep 2
  done
done
WORK="$work" python3 - <<'PY'
import json,os,secrets
from pathlib import Path
w=Path(os.environ['WORK']); sql=[]; checks=[]
for service in ['auth','project','github','jira','meeting','submission']:
    db='researchtrack_dr_'+service
    payload='Synthetic '+service+' '+secrets.token_hex(12000)
    sql += [f'CREATE DATABASE `{db}` CHARACTER SET utf8mb4; USE `{db}`;',
      'CREATE TABLE dr_business (Id INT PRIMARY KEY, Payload LONGTEXT, ExactAt DATETIME(6), BinaryValue BLOB) ENGINE=InnoDB;',
      f"INSERT INTO dr_business VALUES (1,'{payload}','2026-10-12 01:02:03.123456',X'0001FEFF');",
      'CREATE TABLE dr_outbox (EventId CHAR(36) PRIMARY KEY, Status VARCHAR(16), Attempts INT, NextAttempt DATETIME(6), Payload TEXT) ENGINE=InnoDB;',
      'CREATE TABLE dr_inbox (EventId CHAR(36) PRIMARY KEY, PayloadHash CHAR(64)) ENGINE=InnoDB;',
      'CREATE TABLE dr_audit (Id INT NOT NULL) ENGINE=InnoDB;',
      'CREATE VIEW dr_business_view AS SELECT Id,ExactAt FROM dr_business;',
      'DELIMITER //',
      'CREATE PROCEDURE dr_count() BEGIN SELECT COUNT(*) FROM dr_business; END//',
      'CREATE TRIGGER dr_business_updated AFTER UPDATE ON dr_business FOR EACH ROW INSERT INTO dr_audit VALUES (NEW.Id)//',
      'CREATE EVENT dr_disabled_event ON SCHEDULE EVERY 1 DAY STARTS \'2036-01-01 00:00:00\' DISABLE DO SELECT 1//',
      'DELIMITER ;']
    for i,status in enumerate(['PENDING','PUBLISHING','PUBLISHED','FAILED'],1):
        event=f'10000000-0000-0000-0000-{i:012d}'
        sql += [f"INSERT INTO dr_outbox VALUES ('{event}','{status}',{i},'2026-10-12 01:02:03.654321','synthetic-{status}');"]
    sql += ["INSERT INTO dr_inbox VALUES ('10000000-0000-0000-0000-000000000003',REPEAT('a',64));"]
    checks += [f"SELECT '{service}',SHA2(CONCAT(Payload,DATE_FORMAT(ExactAt,'%Y-%m-%d %H:%i:%s.%f'),HEX(BinaryValue)),256) FROM `{db}`.dr_business;",
               f"SELECT '{service}',EventId,Status,Attempts,DATE_FORMAT(NextAttempt,'%f'),Payload FROM `{db}`.dr_outbox ORDER BY EventId;",
               f"SELECT '{service}',EventId,PayloadHash FROM `{db}`.dr_inbox;"]
(w/'seed.sql').write_text('\n'.join(sql)+'\n')
(w/'check.sql').write_text('\n'.join(checks)+'\n')
PY
mysql_exec() { docker exec -i "$1" sh -c 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" exec mysql -uroot --binary-mode=1 --batch --skip-column-names'; }
mysql_exec "$source_id" < "$work/seed.sql" > "$evidence/seed.log" 2>&1
mysql_exec "$source_id" < "$work/check.sql" > "$evidence/source-rows.tsv"
started=$SECONDS
docker exec "$source_id" bash /opt/researchtrack/scripts/backup-mysql.sh > "$evidence/backup.log" 2>&1
backup_seconds=$((SECONDS-started))
backup_path="$(docker exec "$source_id" sh -c 'find /data/researchtrack/backups -name "researchtrack-mysql-*.sql.gz" -type f')"
[[ -n "$backup_path" && "$backup_path" != *$'\n'* ]] || { echo 'FAIL: expected one completed backup.' >&2; exit 1; }
docker cp "$source_id:$backup_path" "$evidence/synthetic-six-databases.sql.gz"
gzip -t "$evidence/synthetic-six-databases.sql.gz"
started=$SECONDS
gzip -dc "$evidence/synthetic-six-databases.sql.gz" | mysql_exec "$target_id" > "$evidence/restore.log" 2>&1
restore_seconds=$((SECONDS-started))
mysql_exec "$target_id" < "$work/check.sql" > "$evidence/restored-rows.tsv"
cmp "$evidence/source-rows.tsv" "$evidence/restored-rows.tsv"
for service in auth project github jira meeting submission; do
  db="researchtrack_dr_$service"
  # shellcheck disable=SC2016 # Backticks quote SQL identifiers, not shell commands.
  printf 'CALL `%s`.dr_count(); UPDATE `%s`.dr_business SET Payload=Payload WHERE Id=1; SELECT COUNT(*) FROM `%s`.dr_audit;\n' "$db" "$db" "$db" | \
    mysql_exec "$target_id" > "$work/objects-$service.tsv"
  [[ "$(tr '\n' ',' < "$work/objects-$service.tsv")" == '1,1,' ]] || exit 1
  count="$(printf "SELECT COUNT(*) FROM information_schema.EVENTS WHERE EVENT_SCHEMA='%s' AND STATUS='DISABLED';\n" "$db" | mysql_exec "$target_id")"
  [[ "$count" == 1 ]] || exit 1
done
if printf "INSERT INTO researchtrack_dr_jira.dr_inbox SELECT * FROM researchtrack_dr_jira.dr_inbox;\n" | mysql_exec "$target_id" > "$evidence/duplicate-rejection.log" 2>&1; then
  echo 'FAIL: restored synthetic receipt key accepted a duplicate.' >&2; exit 1
fi
grep -q '1062' "$evidence/duplicate-rejection.log"
CID_SOURCE="$source_id" CID_TARGET="$target_id" EVIDENCE="$evidence" BACKUP_SECONDS="$backup_seconds" RESTORE_SECONDS="$restore_seconds" python3 - <<'PY'
import hashlib,json,os,subprocess
from pathlib import Path
e=Path(os.environ['EVIDENCE'])
ids=[os.environ['CID_SOURCE'],os.environ['CID_TARGET']]
containers=[]
for cid in ids:
 c=json.loads(subprocess.check_output(['docker','inspect',cid]))[0]
 version=subprocess.check_output(['docker','exec',cid,'sh','-c','MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot -Nse "SELECT VERSION()"'],text=True).strip()
 containers.append(dict(id=cid,imageId=c['Image'],network=c['HostConfig']['NetworkMode'],version=version))
r=dict(result='PASS',scope='synthetic schemas; not EF/application replay or production DR',databases=6,businessRows=6,outboxRows=24,receiptRows=6,
       preserves=['payload hashes','DATETIME(6)','binary bytes','pending/publishing/published/failed states','views','procedures','triggers','disabled events','receipt uniqueness'],
       backupSeconds=int(os.environ['BACKUP_SECONDS']),restoreSeconds=int(os.environ['RESTORE_SECONDS']),
       gzipBytes=(e/'synthetic-six-databases.sql.gz').stat().st_size,sha256=hashlib.sha256((e/'synthetic-six-databases.sql.gz').read_bytes()).hexdigest(),containers=containers)
(e/'result.json').write_text(json.dumps(r,indent=2)+'\n')
print('PASS: six real MySQL synthetic databases restored; rows, precision, bytes, objects and duplicate key checked.')
print(f"Fixture-only backup={r['backupSeconds']}s restore={r['restoreSeconds']}s compressed={r['gzipBytes']} bytes; not a production RTO.")
PY
