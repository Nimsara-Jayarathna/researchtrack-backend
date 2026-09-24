#!/usr/bin/env bash
# Installs/refreshes the backup and disk-usage timers (idempotent).
set -euo pipefail

unit_dir=/etc/systemd/system

write_unit() {
  local path="$1" content="$2"
  [[ -f "$path" && "$(cat "$path")" == "$content" ]] && return 0
  printf '%s\n' "$content" > "$path"
  changed=true
}
changed=false

write_unit "$unit_dir/researchtrack-mysql-backup.service" "[Unit]
Description=ResearchTrack MySQL backup
After=docker.service
Requires=docker.service

[Service]
Type=oneshot
ExecStart=/opt/researchtrack/scripts/backup-mysql.sh"

write_unit "$unit_dir/researchtrack-mysql-backup.timer" "[Unit]
Description=Daily ResearchTrack MySQL backup

[Timer]
OnCalendar=*-*-* 02:30:00 UTC
RandomizedDelaySec=15m
Persistent=true

[Install]
WantedBy=timers.target"

write_unit "$unit_dir/researchtrack-disk-check.service" "[Unit]
Description=ResearchTrack disk usage warning

[Service]
Type=oneshot
ExecStart=/opt/researchtrack/scripts/disk-check.sh 80
SuccessExitStatus=1"

write_unit "$unit_dir/researchtrack-disk-check.timer" "[Unit]
Description=ResearchTrack disk usage check every 15 minutes

[Timer]
OnCalendar=*:0/15
Persistent=true

[Install]
WantedBy=timers.target"

[[ "$changed" == true ]] && systemctl daemon-reload
systemctl enable --now researchtrack-mysql-backup.timer researchtrack-disk-check.timer >/dev/null 2>&1
echo "      timers: $(systemctl is-active researchtrack-mysql-backup.timer) backup, $(systemctl is-active researchtrack-disk-check.timer) disk-check"
