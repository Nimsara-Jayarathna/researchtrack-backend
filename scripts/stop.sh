#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root

profile="${1:-all}"
case "$profile" in
  core) services=(gateway auth project) ;;
  integrations) services=(gateway jira github project auth) ;;
  research) services=(gateway submission meeting project auth) ;;
  all) services=(gateway submission meeting jira github project auth) ;;
  *) echo "Usage: ./scripts/stop.sh <core|integrations|research|all>" >&2; exit 1 ;;
esac

for service in "${services[@]}"; do
  pid_file=".run/${service}.pid"
  [[ -f "$pid_file" ]] || continue
  pid="$(cat "$pid_file" 2>/dev/null || true)"
  if [[ -n "$pid" ]] && kill -0 "$pid" 2>/dev/null; then
    kill "$pid" 2>/dev/null || true
    for _ in {1..20}; do
      kill -0 "$pid" 2>/dev/null || break
      sleep 0.1
    done
    kill -9 "$pid" 2>/dev/null || true
    echo "Stopped $service (PID $pid)"
  fi
  rm -f "$pid_file"
done
