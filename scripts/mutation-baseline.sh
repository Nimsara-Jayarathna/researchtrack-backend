#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_ROOT="${ROOT_DIR}/artifacts/mutation"
TARGET="${1:-all}"

usage() {
  cat <<'TXT'
Usage: bash ./scripts/mutation-baseline.sh [all|auth|auth-registration|project|submission|github|jira|gateway|meeting]

Runs ResearchTrack's targeted business-logic mutation suite against dedicated
xUnit v2 mutation harnesses. Normal xUnit v3 unit/mock projects are verified
first with database integration tests excluded. Stryker break thresholds stay at
0 until all service baselines are stabilized and survivor review is complete.
TXT
}

case "$TARGET" in
  all|auth|auth-registration|project|submission|github|jira|gateway|meeting) ;;
  -h|--help) usage; exit 0 ;;
  *) echo "Unknown mutation target: $TARGET" >&2; usage >&2; exit 2 ;;
esac

command -v dotnet >/dev/null 2>&1 || { echo "dotnet SDK is required." >&2; exit 127; }
mkdir -p "$ARTIFACT_ROOT"
TOOL_MANIFEST="${ROOT_DIR}/.config/dotnet-tools.json"

echo "Restoring repository-local .NET tools (EF + Stryker + reporting tools) ..."
dotnet tool restore --tool-manifest "$TOOL_MANIFEST"
(
  cd "$ROOT_DIR"
  dotnet tool run dotnet-stryker -- --help >/dev/null
) || { echo "dotnet-stryker could not be resolved from ${TOOL_MANIFEST}." >&2; exit 127; }

normal_project_for() {
  case "$1" in
    auth) echo "tests/ResearchTrack.AuthService.Tests/ResearchTrack.AuthService.Tests.csproj" ;;
    project) echo "tests/ResearchTrack.ProjectService.Tests/ResearchTrack.ProjectService.Tests.csproj" ;;
    submission) echo "tests/ResearchTrack.SubmissionService.Tests/ResearchTrack.SubmissionService.Tests.csproj" ;;
    github) echo "tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj" ;;
    jira) echo "tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj" ;;
    gateway) echo "tests/ResearchTrack.Gateway.Tests/ResearchTrack.Gateway.Tests.csproj" ;;
    meeting) echo "tests/ResearchTrack.MeetingService.Tests/ResearchTrack.MeetingService.Tests.csproj" ;;
  esac
}

harness_project_for() {
  case "$1" in
    auth) echo "tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj" ;;
    project) echo "tests/ResearchTrack.ProjectService.MutationTests/ResearchTrack.ProjectService.MutationTests.csproj" ;;
    submission) echo "tests/ResearchTrack.SubmissionService.MutationTests/ResearchTrack.SubmissionService.MutationTests.csproj" ;;
    github) echo "tests/ResearchTrack.GitHubService.MutationTests/ResearchTrack.GitHubService.MutationTests.csproj" ;;
    jira) echo "tests/ResearchTrack.JiraService.MutationTests/ResearchTrack.JiraService.MutationTests.csproj" ;;
    gateway) echo "tests/ResearchTrack.Gateway.MutationTests/ResearchTrack.Gateway.MutationTests.csproj" ;;
    meeting) echo "tests/ResearchTrack.MeetingService.MutationTests/ResearchTrack.MeetingService.MutationTests.csproj" ;;
  esac
}

harness_dir_for() {
  dirname "$(harness_project_for "$1")"
}

verify_scope_tests() {
  local service="$1"
  echo
  echo "Verifying normal ${service} unit/mock tests (database integration excluded) ..."
  (cd "$ROOT_DIR" && dotnet test "$(normal_project_for "$service")" -c Release --filter "Category!=DatabaseIntegration")
  echo
  echo "Verifying dedicated ${service} mutation harness ..."
  (cd "$ROOT_DIR" && dotnet test "$(harness_project_for "$service")" -c Release)
}

run_config() {
  local service="$1"
  local scope="$2"
  local config="$3"
  local output_dir="${ARTIFACT_ROOT}/${service}/${scope}"
  local harness_dir="$(harness_dir_for "$service")"
  echo
  echo "------------------------------------------------------------"
  echo "${service} mutation subscope: ${scope}"
  echo "------------------------------------------------------------"
  rm -rf "$output_dir"
  mkdir -p "$output_dir"
  (
    cd "${ROOT_DIR}/${harness_dir}"
    dotnet tool run dotnet-stryker -- \
      --config-file "configs/${config}" \
      --output "$output_dir"
  )
}

run_auth() {
  verify_scope_tests auth
  run_config auth password-policy password-policy.json
  run_config auth authentication authentication.json
  run_config auth password-reset password-reset.json
  run_config auth registration registration.json
  run_config auth user-account user-account.json
  run_config auth user-directory user-directory.json
}

run_auth_registration() {
  verify_scope_tests auth
  run_config auth registration registration.json
}

run_project() {
  verify_scope_tests project
  run_config project rules rules.json
  run_config project project-service project-service.json
  run_config project dashboard dashboard.json
}

run_submission() {
  verify_scope_tests submission
  run_config submission rules rules.json
  run_config submission requirement-service requirement-service.json
  run_config submission submission-service submission-service.json
}

run_github() {
  verify_scope_tests github
  run_config github repository-url repository-url.json
  run_config github webhook-signature webhook-signature.json
  run_config github access-token access-token.json
  run_config github installation-state installation-state.json
  run_config github repository-link repository-link.json
  run_config github webhook-event webhook-event.json
  run_config github reconciliation reconciliation.json
}

run_jira() {
  verify_scope_tests jira
  run_config jira issue-mapper issue-mapper.json
  run_config jira sync-state sync-state.json
  run_config jira issue-query issue-query.json
  run_config jira sprint-progress sprint-progress.json
  run_config jira webhook webhook.json
}

run_gateway() {
  verify_scope_tests gateway
  run_config gateway performance-rate-limit performance-rate-limit.json
  run_config gateway trusted-proxy trusted-proxy.json
}

run_meeting() {
  verify_scope_tests meeting
  run_config meeting channel-service channel-service.json
  run_config meeting record-service record-service.json
}

case "$TARGET" in
  auth) run_auth ;;
  auth-registration) run_auth_registration ;;
  project) run_project ;;
  submission) run_submission ;;
  github) run_github ;;
  jira) run_jira ;;
  gateway) run_gateway ;;
  meeting) run_meeting ;;
  all)
    run_auth
    run_project
    run_submission
    run_github
    run_jira
    run_gateway
    run_meeting
    ;;
esac

PYTHON_CMD=()
if command -v python3 >/dev/null 2>&1 && python3 -c 'import sys; raise SystemExit(sys.version_info.major != 3)' >/dev/null 2>&1; then
  PYTHON_CMD=(python3)
elif command -v python >/dev/null 2>&1 && python -c 'import sys; raise SystemExit(sys.version_info.major != 3)' >/dev/null 2>&1; then
  PYTHON_CMD=(python)
elif command -v py >/dev/null 2>&1 && py -3 -c 'import sys; raise SystemExit(sys.version_info.major != 3)' >/dev/null 2>&1; then
  PYTHON_CMD=(py -3)
fi

if (( ${#PYTHON_CMD[@]} == 0 )); then
  echo "WARNING: Python 3 was not found; Stryker reports are complete, but mutation-summary.md was not regenerated." >&2
else
  "${PYTHON_CMD[@]}" "${ROOT_DIR}/scripts/mutation-summary.py" "$ARTIFACT_ROOT"
fi

echo
echo "Mutation run completed."
echo "Reports: ${ARTIFACT_ROOT}/"
[[ -f "${ARTIFACT_ROOT}/mutation-summary.md" ]] && echo "Summary: ${ARTIFACT_ROOT}/mutation-summary.md"
