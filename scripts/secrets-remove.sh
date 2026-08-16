#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
service="${1:-}"
key="${2:-}"
if [[ -z "$service" || -z "$key" ]]; then
  echo "Usage: ./scripts/secrets-remove.sh <gateway|auth|project|github|jira|meeting|submission> <Configuration:Key>" >&2
  exit 1
fi
project="$(rt_service_project "$service")"
dotnet user-secrets remove --project "$project" "$key"
