#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command curl

profile="${1:-all}"
case "$profile" in
  core) services=(gateway auth project) ;;
  integrations) services=(gateway auth project github jira) ;;
  research) services=(gateway auth project meeting submission) ;;
  all) services=(gateway auth project github jira meeting submission) ;;
  *) echo "Usage: ./scripts/health.sh <core|integrations|research|all>" >&2; exit 1 ;;
esac

printf '%-12s %-10s %-10s\n' "SERVICE" "LIVE" "READY"
printf '%-12s %-10s %-10s\n' "------------" "----------" "----------"
failed=0
for service in "${services[@]}"; do
  port="$(rt_service_port "$service")"
  live="FAIL"; ready="FAIL"
  curl -fsS --max-time 2 "http://localhost:$port/health/live" >/dev/null 2>&1 && live="OK"
  curl -fsS --max-time 2 "http://localhost:$port/health/ready" >/dev/null 2>&1 && ready="OK"
  printf '%-12s %-10s %-10s\n' "$service" "$live" "$ready"
  [[ "$live" == "OK" ]] || failed=1
  # Gateway has no DB; business services should be ready once DB is configured.
  [[ "$ready" == "OK" ]] || failed=1
done
exit "$failed"
