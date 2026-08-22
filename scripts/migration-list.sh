#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/migration-list.sh <service>" >&2
  exit 1
fi

rt_load_dev_env "$service"
rt_validate_db_environment

project="$(rt_service_project "$service")"
context="$(rt_service_context "$service")"

dotnet ef migrations list \
  --project "$project" \
  --startup-project "$project" \
  --context "$context"
