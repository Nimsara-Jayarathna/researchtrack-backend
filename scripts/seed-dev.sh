#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root; rt_require_command dotnet
service="${1:-}"
if [[ -z "$service" ]]; then echo "Usage: ./scripts/seed-dev.sh <service>" >&2; exit 1; fi
rt_load_dev_env "$service"
[[ "${service,,}" == "gateway" ]] || rt_validate_db_environment
exec dotnet run --project tools/ResearchTrack.DevSeeder/ResearchTrack.DevSeeder.csproj -- "$service"
