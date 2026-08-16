#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
rt_load_dev_env
rt_validate_db_environment

service="${1:-}"
name="${2:-}"
if [[ -z "$service" || -z "$name" ]]; then
  echo "Usage: ./scripts/migration-add.sh <service> <MigrationName>" >&2
  exit 1
fi

project="$(rt_service_project "$service")"
context="$(rt_service_context "$service")"
export ConnectionStrings__DefaultConnection="$(rt_db_connection "$service" dev)"

dotnet ef migrations add "$name" \
  --project "$project" \
  --startup-project "$project" \
  --context "$context" \
  --output-dir Persistence/Migrations
