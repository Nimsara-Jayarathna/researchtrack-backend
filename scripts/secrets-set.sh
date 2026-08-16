#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

service="${1:-}"
key="${2:-}"
if [[ -z "$service" || -z "$key" ]]; then
  echo "Usage: ./scripts/secrets-set.sh <gateway|auth|project|github|jira|meeting|submission> <Configuration:Key>" >&2
  exit 1
fi
project="$(rt_service_project "$service")"
read -r -s -p "Secret value for '$key': " value
echo
if [[ -z "$value" ]]; then
  echo "Secret value cannot be empty." >&2
  exit 1
fi
dotnet user-secrets set --project "$project" "$key" "$value" >/dev/null
echo "Secret '$key' stored for $service in the local ASP.NET User Secrets store (outside the repository)."
