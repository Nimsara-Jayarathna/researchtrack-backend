#!/usr/bin/env bash
# Shell-only simulation; exercises CLI routing, explicit file scope and failure behavior.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT
mkdir -p "$TEMP/bin"
cat > "$TEMP/bin/dotnet" <<'FAKE'
#!/usr/bin/env bash
set -euo pipefail
printf '%s\n' "$*" >> "$MOCK_DOTNET_CALLS"
if [[ "${1:-}" == --version ]]; then echo 10.0.401; exit 0; fi
if [[ "${1:-}" == csharpier && "${2:-}" == --version ]]; then echo 1.3.0; exit 0; fi
if [[ "${1:-}" == tool && "${2:-}" == restore ]]; then
  [[ "${MOCK_FAIL_RESTORE:-0}" == 1 ]] && exit 17
  exit 0
fi
if [[ "${1:-}" == csharpier ]]; then
  for a in "$@"; do
    case "$a" in
      */Migrations/*|*.csproj|*.sln|*/tests/*|*/tools/*)
        echo "ERROR: unsafe CSharpier path $a" >&2; exit 18;;
    esac
  done
  if [[ "${MOCK_FAIL_FORMAT:-0}" == 1 && "${2:-}" == check ]]; then
    echo "Mock formatting noncompliance" >&2; exit 1
  fi
  exit 0
fi
if [[ "${1:-}" == restore || "${1:-}" == format || "${1:-}" == build ]]; then
  [[ "${MOCK_FAIL_BUILD:-0}" == 1 && "${1:-}" == build ]] && exit 25
  exit 0
fi
echo "Unknown mock invocation $*" >&2; exit 37
FAKE
chmod +x "$TEMP/bin/dotnet"
export PATH="$TEMP/bin:$PATH" MOCK_DOTNET_CALLS="$TEMP/invocations.txt"
mkdir -p "$ROOT/artifacts/code-quality"
BASE="$ROOT/artifacts/code-quality"

bash "$ROOT/scripts/code-quality.sh" doctor > "$TEMP/doctor.log"
bash "$ROOT/scripts/code-quality.sh" setup > "$TEMP/setup.log"
bash "$ROOT/scripts/code-quality.sh" fix smoke-fix > "$TEMP/fix.log"
bash "$ROOT/scripts/code-quality.sh" check smoke-check > "$TEMP/check.log"
bash "$ROOT/scripts/code-quality.sh" analyze smoke-analyze > "$TEMP/analyze.log"
# Check noncompliance fails; no false-green format checks.
if MOCK_FAIL_FORMAT=1 bash "$ROOT/scripts/code-quality.sh" check smoke-bad-format > "$TEMP/bad-format.log" 2>&1; then
  echo 'FAIL: check succeeded despite simulated formatting violations' >&2; exit 1
fi
if MOCK_FAIL_BUILD=1 bash "$ROOT/scripts/code-quality.sh" analyze smoke-bad-build > "$TEMP/bad-build.log" 2>&1; then
  echo 'FAIL: analyze succeeded despite simulated compiler failure' >&2; exit 1
fi
# The source list in the tool invocation excludes migrations and project XML.
if grep -E 'csharpier (check|format).*Migrations/|csharpier (check|format).*\.csproj' "$MOCK_DOTNET_CALLS"; then
  echo 'FAIL: formatter saw a protected source file' >&2; exit 1
fi
# Verify only production C# file paths are supplied to CSharpier, not directory scans.
grep -q '^csharpier check .*\.cs' "$MOCK_DOTNET_CALLS" || {
  echo 'FAIL: no explicit file-path formatting checks were executed' >&2; exit 1;
}
grep -q '^build .*ResearchTrack.AuthService.csproj' "$MOCK_DOTNET_CALLS" || {
  echo 'FAIL: production build was never invoked' >&2; exit 1;
}
# Ensure every test run produced parseable machine-readable step state.
for id in smoke-fix smoke-check smoke-analyze smoke-bad-format smoke-bad-build; do
  [[ -s "$BASE/$id/manifest.json" && -s "$BASE/$id/status.tsv" ]] || {
    echo "FAIL: missing report from $id" >&2; exit 1;
  }
done
if command -v python3 >/dev/null 2>&1; then
  python3 - <<PY
import json
from pathlib import Path
for id in ['smoke-fix', 'smoke-check', 'smoke-analyze', 'smoke-bad-format', 'smoke-bad-build']:
    j=json.loads((Path('$BASE')/id/'manifest.json').read_text())
    assert j['production_projects']==8
    assert j['eligible_files']>100
    assert j['steps']
assert json.loads((Path('$BASE')/'smoke-bad-format'/'manifest.json').read_text())['failures']>0
PY
fi
printf 'PASS: doctor, pinned tool setup, CSharpier fix/check, style analysis, failure gates, protected paths, reports.\n'
echo 'NOTE: Mock dotnet was used. Actual formatting and compilation must run with .NET 10 and NuGet access.'
