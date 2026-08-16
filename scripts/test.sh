#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root
rt_require_command dotnet

scope="${1:-all}"

if [[ "$scope" == "integration" ]]; then
  rt_load_dev_env
  rt_validate_db_environment

  export RESEARCHTRACK_TEST_AUTH_CONNECTION="$(rt_db_connection auth test)"
  export RESEARCHTRACK_TEST_PROJECT_CONNECTION="$(rt_db_connection project test)"
  export RESEARCHTRACK_TEST_GITHUB_CONNECTION="$(rt_db_connection github test)"
  export RESEARCHTRACK_TEST_JIRA_CONNECTION="$(rt_db_connection jira test)"
  export RESEARCHTRACK_TEST_MEETING_CONNECTION="$(rt_db_connection meeting test)"
  export RESEARCHTRACK_TEST_SUBMISSION_CONNECTION="$(rt_db_connection submission test)"

  dotnet test ResearchTrack.sln \
    -c "${CONFIGURATION:-Debug}" \
    --filter "Category=DatabaseIntegration" \
    --collect:"XPlat Code Coverage"
  exit 0
fi

if [[ "$scope" == "all" ]]; then
  target="ResearchTrack.sln"
else
  case "${scope,,}" in
    gateway) target="tests/ResearchTrack.Gateway.Tests/ResearchTrack.Gateway.Tests.csproj" ;;
    auth|project|github|jira|meeting|submission)
      cap="${scope^}"
      [[ "${scope,,}" == "github" ]] && cap="GitHub"
      [[ "${scope,,}" == "jira" ]] && cap="Jira"
      target="tests/ResearchTrack.${cap}Service.Tests/ResearchTrack.${cap}Service.Tests.csproj"
      ;;
    *)
      echo "Usage: ./scripts/test.sh <all|gateway|auth|project|github|jira|meeting|submission|integration>" >&2
      exit 1
      ;;
  esac
fi

dotnet test "$target" \
  -c "${CONFIGURATION:-Debug}" \
  --filter "Category!=DatabaseIntegration" \
  --collect:"XPlat Code Coverage"
