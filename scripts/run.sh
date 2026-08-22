#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission> [--no-build]" >&2
  exit 1
fi
shift

no_build=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      no_build=true
      shift
      ;;
    -h|--help)
      echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission> [--no-build]"
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission> [--no-build]" >&2
      exit 1
      ;;
  esac
done

rt_load_dev_env "$service"
project="$(rt_service_project "$service")"
port="$(rt_service_port "$service")"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:$port}"

if [[ "${service,,}" == "gateway" ]]; then
  rt_gateway_env
else
  rt_validate_db_environment
fi

echo "Starting $service on $ASPNETCORE_URLS"

run_args=(run --project "$project" --no-launch-profile)
if [[ "$no_build" == true ]]; then
  run_args+=(--no-build)
fi

exec dotnet "${run_args[@]}"
