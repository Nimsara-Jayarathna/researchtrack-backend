#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

dotnet tool restore
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration&Category!=E2E" --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet format ResearchTrack.sln --verify-no-changes --no-restore

echo "ResearchTrack quality checks passed (database integration and browser E2E tests are opt-in: ./scripts/test.sh integration / ./scripts/test.sh e2e)."
