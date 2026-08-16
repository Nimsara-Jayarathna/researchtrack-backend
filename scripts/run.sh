#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
rt_load_dev_env

service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission>" >&2
  exit 1
fi

project="$(rt_service_project "$service")"
port="$(rt_service_port "$service")"
export ASPNETCORE_URLS="http://localhost:$port"

if [[ "${service,,}" == "gateway" ]]; then
  rt_gateway_env
else
  rt_validate_db_environment
  export ConnectionStrings__DefaultConnection="$(rt_db_connection "$service" dev)"
fi

echo "Starting $service on http://localhost:$port"
exec dotnet run --project "$project" --no-launch-profile
