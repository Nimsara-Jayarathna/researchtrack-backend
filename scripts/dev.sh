#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

profile="${1:-core}"
if [[ $# -gt 0 ]]; then
  shift
fi

case "$profile" in
  core) services=(auth project gateway) ;;
  integrations) services=(auth project github jira gateway) ;;
  research) services=(auth project meeting submission gateway) ;;
  all) services=(auth project github jira meeting submission gateway) ;;
  *)
    echo "Usage: ./scripts/dev.sh <core|integrations|research|all> [--no-build]" >&2
    exit 1
    ;;
esac

no_build=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      no_build=true
      shift
      ;;
    -h|--help)
      echo "Usage: ./scripts/dev.sh <core|integrations|research|all> [--no-build]"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Usage: ./scripts/dev.sh <core|integrations|research|all> [--no-build]" >&2
      exit 1
      ;;
  esac
done

if [[ "$no_build" == false ]]; then
  echo "Building backend once before starting the service profile..."
  ./scripts/build.sh
fi

mkdir -p .run/logs

start_service() {
  local service="$1" pid_file=".run/${service}.pid" log_file=".run/logs/${service}.log"
  local old_pid pid

  if [[ -f "$pid_file" ]]; then
    old_pid="$(cat "$pid_file" 2>/dev/null || true)"
    if [[ -n "$old_pid" ]] && kill -0 "$old_pid" 2>/dev/null; then
      echo "Already running: $service (PID $old_pid)"
      return
    fi
    rm -f "$pid_file"
  fi

  # A service profile always launches from the single build completed above
  # (or from the build already completed by start-all.sh). This prevents
  # concurrent dotnet run builds from racing over shared BuildingBlocks output.
  nohup ./scripts/run.sh "$service" --no-build >"$log_file" 2>&1 &
  pid=$!
  echo "$pid" > "$pid_file"
  echo "Started $service (PID $pid) -> $log_file"
}

for service in "${services[@]}"; do
  start_service "$service"
done

echo
echo "Waiting briefly for services to start..."
sleep 3
./scripts/health.sh "$profile" || true

echo
echo "Use ./scripts/stop.sh $profile to stop these processes."
