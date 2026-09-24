#!/usr/bin/env bash
# Logs a journal warning before the OS or data disk fills up. Prometheus/Kafka
# retention limits are the primary control; this is the early warning.
set -euo pipefail
warn_at="${1:-80}"
status=0
for mount in / /data/researchtrack; do
  used="$(df --output=pcent "$mount" | tail -1 | tr -dc '0-9')"
  if (( used >= warn_at )); then
    logger -p user.warning -t researchtrack-disk "$mount is ${used}% full"
    echo "WARNING: $mount is ${used}% full" >&2
    status=1
  fi
done
exit "$status"
