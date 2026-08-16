#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet
dotnet clean ResearchTrack.sln
find . -type d \( -name bin -o -name obj -o -name TestResults \) -prune -exec rm -rf {} +
rm -rf coverage coverage-results coverage-report artifacts .run
echo "Removed build, test, coverage, generated migration SQL, and local process artifacts."
