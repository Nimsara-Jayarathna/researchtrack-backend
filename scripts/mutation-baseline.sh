#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_ROOT="${ROOT_DIR}/artifacts/mutation"
TARGET="${1:-all}"

usage() {
  cat <<'TXT'
Usage: bash ./scripts/mutation-baseline.sh [all|project|auth|submission]

Runs Pamudi's targeted mutation baseline against dedicated xUnit v2 mutation
harnesses. Auth runs six feature-level mutation subscopes (policy, authentication,
password reset, registration, user account, and user directory). The main xUnit v3 unit/mock projects remain unchanged and continue
to own normal CI coverage. No database integration tests are part of these
mutation harnesses.
TXT
}

case "$TARGET" in
  all|project|auth|submission) ;;
  -h|--help) usage; exit 0 ;;
  *) echo "Unknown mutation target: $TARGET" >&2; usage >&2; exit 2 ;;
esac

command -v dotnet >/dev/null 2>&1 || {
  echo "dotnet SDK is required to run mutation testing." >&2
  exit 127
}

mkdir -p "$ARTIFACT_ROOT"

TOOL_MANIFEST="${ROOT_DIR}/.config/dotnet-tools.json"

echo "Restoring repository-local .NET tools (EF + Stryker + reporting tools) ..."
dotnet tool restore --tool-manifest "$TOOL_MANIFEST"

# Fail early with a clear message if the mutation tool is not actually available
# from the canonical repository-local manifest.
(
  cd "$ROOT_DIR"
  dotnet tool run dotnet-stryker -- --help >/dev/null
) || {
  echo "dotnet-stryker could not be resolved from ${TOOL_MANIFEST}." >&2
  exit 127
}

run_target() {
  local name="$1"
  local harness_dir="$2"
  local project_file="$3"
  local config_file="${4:-stryker-config.json}"
  local output_dir="${5:-${ARTIFACT_ROOT}/${name}}"

  echo
  echo "============================================================"
  echo "Mutation baseline: ${name}"
  echo "Harness: ${harness_dir}"
  echo "Config: ${config_file}"
  echo "============================================================"

  rm -rf "$output_dir"
  mkdir -p "$output_dir"

  echo "Verifying dedicated mutation harness before Stryker ..."
  (
    cd "$ROOT_DIR"
    dotnet test "$project_file" -c Release
  )

  (
    cd "${ROOT_DIR}/${harness_dir}"
    dotnet tool run dotnet-stryker -- \
      --config-file "$config_file" \
      --output "$output_dir"
  )
}

run_auth_scope() {
  local scope="$1"
  local config="$2"
  local harness_dir="tests/ResearchTrack.AuthService.MutationTests"
  local project_file="tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj"
  local output_dir="${ARTIFACT_ROOT}/auth/${scope}"

  echo
  echo "------------------------------------------------------------"
  echo "Auth mutation subscope: ${scope}"
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

run_auth_baseline() {
  local harness_dir="tests/ResearchTrack.AuthService.MutationTests"
  local project_file="tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj"

  echo
  echo "============================================================"
  echo "Mutation baseline: auth (complete feature/service layer)"
  echo "Harness: ${harness_dir}"
  echo "============================================================"

  rm -rf "${ARTIFACT_ROOT}/auth"
  mkdir -p "${ARTIFACT_ROOT}/auth"

  echo "Verifying complete dedicated Auth mutation harness before Stryker ..."
  (
    cd "$ROOT_DIR"
    dotnet test "$project_file" -c Release
  )

  run_auth_scope "password-policy" "password-policy.json"
  run_auth_scope "authentication" "authentication.json"
  run_auth_scope "password-reset" "password-reset.json"
  run_auth_scope "registration" "registration.json"
  run_auth_scope "user-account" "user-account.json"
  run_auth_scope "user-directory" "user-directory.json"
}

if [[ "$TARGET" == "all" || "$TARGET" == "project" ]]; then
  run_target \
    "project" \
    "tests/ResearchTrack.ProjectService.MutationTests" \
    "tests/ResearchTrack.ProjectService.MutationTests/ResearchTrack.ProjectService.MutationTests.csproj"
fi

if [[ "$TARGET" == "all" || "$TARGET" == "auth" ]]; then
  run_auth_baseline
fi

if [[ "$TARGET" == "all" || "$TARGET" == "submission" ]]; then
  run_target \
    "submission" \
    "tests/ResearchTrack.SubmissionService.MutationTests" \
    "tests/ResearchTrack.SubmissionService.MutationTests/ResearchTrack.SubmissionService.MutationTests.csproj"
fi

if command -v python3 >/dev/null 2>&1; then
  PYTHON_CMD="python3"
elif command -v python >/dev/null 2>&1; then
  PYTHON_CMD="python"
else
  echo "Python 3 is required to generate the mutation summary." >&2
  exit 127
fi

"$PYTHON_CMD" "${ROOT_DIR}/scripts/mutation-summary.py" "$ARTIFACT_ROOT"

echo
echo "Mutation baseline completed."
echo "Reports: ${ARTIFACT_ROOT}/"
echo "Summary: ${ARTIFACT_ROOT}/mutation-summary.md"
