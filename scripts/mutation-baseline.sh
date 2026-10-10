#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACT_ROOT="${ROOT_DIR}/artifacts/mutation"
TARGET="${1:-all}"

usage() {
  cat <<'TXT'
Usage: bash ./scripts/mutation-baseline.sh [all|project|auth|auth-registration|submission]

Runs Pamudi's targeted mutation suite against dedicated mutation harnesses.
Auth runs six feature-level subscopes. `auth-registration` reruns only the
RegistrationService scope after verifying both the normal Auth unit/mock suite
and the dedicated Auth mutation harness. Database integration tests are never
required for mutation verification.
TXT
}

case "$TARGET" in
  all|project|auth|auth-registration|submission) ;;
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
  (cd "$ROOT_DIR" && dotnet test "$project_file" -c Release)

  (
    cd "${ROOT_DIR}/${harness_dir}"
    dotnet tool run dotnet-stryker -- \
      --config-file "$config_file" \
      --output "$output_dir"
  )
}

verify_auth_tests() {
  echo
  echo "Verifying normal Auth unit/mock tests (database integration excluded) ..."
  (
    cd "$ROOT_DIR"
    dotnet test tests/ResearchTrack.AuthService.Tests/ResearchTrack.AuthService.Tests.csproj \
      -c Release \
      --filter "Category!=DatabaseIntegration"
  )

  echo
  echo "Verifying dedicated Auth mutation harness ..."
  (
    cd "$ROOT_DIR"
    dotnet test tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj -c Release
  )
}

run_auth_scope() {
  local scope="$1"
  local config="$2"
  local harness_dir="tests/ResearchTrack.AuthService.MutationTests"
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
  echo
  echo "============================================================"
  echo "Mutation baseline: auth (complete feature/service layer)"
  echo "Harness: tests/ResearchTrack.AuthService.MutationTests"
  echo "============================================================"

  rm -rf "${ARTIFACT_ROOT}/auth"
  mkdir -p "${ARTIFACT_ROOT}/auth"

  verify_auth_tests
  run_auth_scope "password-policy" "password-policy.json"
  run_auth_scope "authentication" "authentication.json"
  run_auth_scope "password-reset" "password-reset.json"
  run_auth_scope "registration" "registration.json"
  run_auth_scope "user-account" "user-account.json"
  run_auth_scope "user-directory" "user-directory.json"
}

run_auth_registration() {
  echo
  echo "============================================================"
  echo "Mutation hardening rerun: auth / registration"
  echo "============================================================"
  verify_auth_tests
  run_auth_scope "registration" "registration.json"
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

if [[ "$TARGET" == "auth-registration" ]]; then
  run_auth_registration
fi

if [[ "$TARGET" == "all" || "$TARGET" == "submission" ]]; then
  run_target \
    "submission" \
    "tests/ResearchTrack.SubmissionService.MutationTests" \
    "tests/ResearchTrack.SubmissionService.MutationTests/ResearchTrack.SubmissionService.MutationTests.csproj"
fi

# Summary generation is portable across macOS/Linux (`python3`), conventional
# Windows installations (`python`), and the Windows Python launcher (`py -3`).
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
if [[ -f "${ARTIFACT_ROOT}/mutation-summary.md" ]]; then
  echo "Summary: ${ARTIFACT_ROOT}/mutation-summary.md"
fi
