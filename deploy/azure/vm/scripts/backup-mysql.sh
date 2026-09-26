#!/usr/bin/env bash
# Daily logical backup of every ResearchTrack service database onto the data
# disk. Keeps the most recent copies only. Off-VM copies are a tracked
# follow-up (see docs/devops/azure-production/IMPLEMENTATION_STATUS.md).
set -Eeuo pipefail

backup_dir=/data/researchtrack/backups
keep=7
file="$backup_dir/researchtrack-mysql-$(date -u +%Y%m%dT%H%M%SZ).sql.gz"
install -d -m 700 "$backup_dir"

umask 077
# Database names come from the container's own environment (mysql.env).
/opt/researchtrack/scripts/compose.sh exec -T mysql sh -c '
  exec mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers \
    --events --set-gtid-purged=OFF --databases \
    "$AUTH_DB_NAME" "$PROJECT_DB_NAME" "$GITHUB_DB_NAME" \
    "$JIRA_DB_NAME" "$MEETING_DB_NAME" "$SUBMISSION_DB_NAME"' </dev/null | gzip -9 > "$file"

gzip -t "$file"
[[ "$(stat -c %s "$file")" -gt 1024 ]] || { echo "Backup is unexpectedly small: $file" >&2; exit 1; }

# Names embed a sortable UTC timestamp.
find "$backup_dir" -maxdepth 1 -name 'researchtrack-mysql-*.sql.gz' | sort -r | tail -n +$((keep + 1)) | xargs -r rm -f
echo "MySQL backup written: $file"
