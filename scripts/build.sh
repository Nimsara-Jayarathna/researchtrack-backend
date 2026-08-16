#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root; rt_require_command dotnet
dotnet build ResearchTrack.sln -c "${CONFIGURATION:-Debug}"
