#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT_HARNESS="tests/ResearchTrack.ProjectService.MutationTests/ResearchTrack.ProjectService.MutationTests.csproj"
AUTH_HARNESS="tests/ResearchTrack.AuthService.MutationTests/ResearchTrack.AuthService.MutationTests.csproj"
SUBMISSION_HARNESS="tests/ResearchTrack.SubmissionService.MutationTests/ResearchTrack.SubmissionService.MutationTests.csproj"
TARGET_FILE="${ROOT_DIR}/src/Services/ResearchTrack.ProjectService/Features/Projects/ProjectMilestonePolicy.cs"

command -v dotnet >/dev/null 2>&1 || {
  echo "dotnet SDK is required." >&2
  exit 127
}

if command -v python3 >/dev/null 2>&1; then
  PYTHON_CMD="python3"
elif command -v python >/dev/null 2>&1; then
  PYTHON_CMD="python"
else
  echo "Python 3 is required for the controlled mutation sanity check." >&2
  exit 127
fi

echo "1/3 Running dedicated mutation harnesses against unmodified production code ..."
(
  cd "$ROOT_DIR"
  dotnet test "$PROJECT_HARNESS" -c Release
  dotnet test "$AUTH_HARNESS" -c Release
  dotnet test "$SUBMISSION_HARNESS" -c Release
)

echo
echo "2/3 Injecting a temporary controlled boundary mutation ..."
BACKUP_FILE="$(mktemp)"
cp "$TARGET_FILE" "$BACKUP_FILE"
restore_source() {
  cp "$BACKUP_FILE" "$TARGET_FILE"
  rm -f "$BACKUP_FILE"
}
trap restore_source EXIT INT TERM

"$PYTHON_CMD" - "$TARGET_FILE" <<'PY'
from pathlib import Path
import sys
p = Path(sys.argv[1])
s = p.read_text()
old = "OpenStatuses.Contains(status) && dueDate < today"
new = "OpenStatuses.Contains(status) && dueDate <= today"
count = s.count(old)
if count != 1:
    raise SystemExit(f"Expected exactly one controlled mutation target, found {count}.")
p.write_text(s.replace(old, new, 1))
PY

set +e
(
  cd "$ROOT_DIR"
  dotnet test "$PROJECT_HARNESS" -c Release --no-restore
)
MUTATED_EXIT=$?
set -e

restore_source
trap - EXIT INT TERM

if [[ "$MUTATED_EXIT" -eq 0 ]]; then
  echo "ERROR: Controlled production mutation was NOT detected by the mutation harness." >&2
  exit 1
fi

echo "Controlled mutation was detected as expected."

echo
echo "3/3 Re-running Project mutation harness after source restoration ..."
(
  cd "$ROOT_DIR"
  dotnet test "$PROJECT_HARNESS" -c Release --no-restore
)

echo
echo "Mutation harness verification PASSED."
echo "The harness passes on correct code, fails on a deliberate boundary defect, and passes again after restoration."
