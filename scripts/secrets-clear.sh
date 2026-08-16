#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
service="${1:-}"
if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/secrets-clear.sh <gateway|auth|project|github|jira|meeting|submission>" >&2
  exit 1
fi
project="$(rt_service_project "$service")"
read -r -p "Clear ALL local User Secrets for $service? Type '$service-clear': " confirm
[[ "$confirm" == "$service-clear" ]] || { echo "Cancelled."; exit 1; }
dotnet user-secrets clear --project "$project"
