#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root; rt_require_command dotnet; rt_load_dev_env
service="${1:-}"
if [[ -z "$service" ]]; then echo "Usage: ./scripts/seed-dev.sh <service>" >&2; exit 1; fi
exec dotnet run --project tools/ResearchTrack.DevSeeder/ResearchTrack.DevSeeder.csproj -- "$service"
