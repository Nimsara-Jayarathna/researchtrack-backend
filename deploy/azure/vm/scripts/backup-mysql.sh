#!/usr/bin/env bash
# Daily logical backup of every ResearchTrack service database onto the data
# disk. Keeps the most recent copies only. Off-VM copies are a tracked
# follow-up (see docs/devops/azure-production/IMPLEMENTATION_STATUS.md).
set -Eeuo pipefail

data_mount=/data/researchtrack
mountpoint -q "$data_mount" || { echo "Backup refused: persistent data disk is not mounted at $data_mount." >&2; exit 1; }
backup_dir="$data_mount/backups"
keep=7
install -d -m 700 "$backup_dir"

umask 077
# Publish only a validated dump. The random suffix also prevents same-second
# manual/timer invocations from overwriting one another's completed backup.
partial="$(mktemp "$backup_dir/.researchtrack-mysql-$(date -u +%Y%m%dT%H%M%SZ).XXXXXX")"
name="${partial##*/}"
file="$backup_dir/${name#.}.sql.gz"
trap 'rm -f "$partial"' EXIT
# Database names come from the container's own environment (mysql.env).
# shellcheck disable=SC2016 # Variables expand inside the MySQL container.
/opt/researchtrack/scripts/compose.sh exec -T mysql sh -c '
  exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers \
    --events --set-gtid-purged=OFF --databases \
    "$AUTH_DB_NAME" "$PROJECT_DB_NAME" "$GITHUB_DB_NAME" \
    "$JIRA_DB_NAME" "$MEETING_DB_NAME" "$SUBMISSION_DB_NAME"' </dev/null | gzip -9 > "$partial"

gzip -t "$partial"
[[ "$(stat -c %s "$partial")" -gt 1024 ]] || { echo "Backup is unexpectedly small; no completed dump was published." >&2; exit 1; }
mv "$partial" "$file"

# Names embed a sortable UTC timestamp.
find "$backup_dir" -maxdepth 1 -name 'researchtrack-mysql-*.sql.gz' | sort -r | tail -n +$((keep + 1)) | xargs -r rm -f
echo "MySQL backup written: $file"
