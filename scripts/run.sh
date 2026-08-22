#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission>" >&2
  exit 1
fi

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
exec dotnet run --project "$project" --no-launch-profile
