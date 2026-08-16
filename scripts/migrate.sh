#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
rt_load_dev_env
rt_validate_db_environment

service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/migrate.sh <service|all>" >&2
  exit 1
fi

migrate_one() {
  local svc="$1" project context
  project="$(rt_service_project "$svc")"
  context="$(rt_service_context "$svc")"
  export ConnectionStrings__DefaultConnection="$(rt_db_connection "$svc" dev)"
  echo "Applying migrations for $svc ($context)..."
  dotnet ef database update \
    --project "$project" \
    --startup-project "$project" \
    --context "$context"
}

if [[ "${service,,}" == "all" ]]; then
  for svc in $(rt_all_db_services); do
    migrate_one "$svc"
  done
else
  migrate_one "$service"
fi
