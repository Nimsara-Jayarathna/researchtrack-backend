#!/usr/bin/env bash
# Shell-based simulated SonarScanner + API test; no actual upload or .NET needed.
set -euo pipefail
SOURCE="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TEMP="$(mktemp -d)"
trap 'rm -rf "$TEMP"' EXIT
ROOT="$TEMP/repository"
mkdir -p "$ROOT/scripts/code-quality" "$TEMP/bin"
cp "$SOURCE/scripts/code-quality/sonar-cloud.sh" "$SOURCE/scripts/code-quality/sonar-metrics.sh" "$ROOT/scripts/code-quality/"
for csproj in \
  src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/ResearchTrack.BuildingBlocks.Api.csproj \
  src/Gateway/ResearchTrack.Gateway/ResearchTrack.Gateway.csproj \
  src/Services/ResearchTrack.AuthService/ResearchTrack.AuthService.csproj \
  src/Services/ResearchTrack.GitHubService/ResearchTrack.GitHubService.csproj \
  src/Services/ResearchTrack.JiraService/ResearchTrack.JiraService.csproj \
  src/Services/ResearchTrack.MeetingService/ResearchTrack.MeetingService.csproj \
  src/Services/ResearchTrack.ProjectService/ResearchTrack.ProjectService.csproj \
  src/Services/ResearchTrack.SubmissionService/ResearchTrack.SubmissionService.csproj; do
    mkdir -p "$ROOT/$(dirname "$csproj")"
    touch "$ROOT/$csproj"
done
cat > "$TEMP/bin/dotnet" <<'FAKE_DOTNET'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == --version ]]; then echo 10.0.401; exit 0; fi
if [[ "$1" == tool && "$2" == list ]]; then
  printf 'Package Id            Version       Commands\n'
  printf '%s\n' '---------------------------------------------'
  printf 'dotnet-sonarscanner    %s        dotnet-sonarscanner\n' "${MOCK_TOOL_VERSION:-11.3.0}"
  exit 0
fi
if [[ "$1" == tool && "$2" == install ]]; then
  shift 2
  while (($#)); do
    if [[ "$1" == --tool-path ]]; then TOOL="$2"; break; fi
    shift
  done
  mkdir -p "$TOOL"
  cp "$MOCK_SCANNER_TEMPLATE" "$TOOL/dotnet-sonarscanner"
  chmod +x "$TOOL/dotnet-sonarscanner"
  exit 0
fi
if [[ "$1" == restore || "$1" == build ]]; then
  printf '%s\n' "$1 $2" >> "$MOCK_DOTNET_CALLS"
  [[ "$1" != build || "${MOCK_FAIL_BUILD:-0}" != 1 ]] || exit 9
  exit 0
fi
exit 21
FAKE_DOTNET
cat > "$TEMP/scanner" <<'FAKE_SCANNER'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == --version ]]; then
  echo 'Please specify begin or end' >&2
  exit 1
fi
printf '%s\n' "$1" >> "$MOCK_SCANNER_CALLS"
if [[ "$1" == begin ]]; then
  for a in "$@"; do
    [[ "$a" != /d:sonar.qualitygate.wait=true ]] || echo 'gate=true' >> "$MOCK_SCANNER_CALLS"
    if [[ "$a" == /d:sonar.scanner.metadataFilePath=* ]]; then
      dest="${a#*=}"
      printf '%s' "$dest" > "$MOCK_METADATA_POINTER"
    fi
  done
fi
if [[ "$1" == end && -s "$MOCK_METADATA_POINTER" ]]; then
  dest="$(cat "$MOCK_METADATA_POINTER")"
  mkdir -p "$(dirname "$dest")"
  printf 'ceTaskId=mock-ce\nprojectKey=sample-project\n' > "$dest"
fi
FAKE_SCANNER
cat > "$TEMP/bin/curl" <<'FAKE_CURL'
#!/usr/bin/env bash
set -euo pipefail
out=''; url=''
args=("$@")
for ((i=0;i<${#args[@]};i++)); do
  [[ "${args[i]}" != -o ]] || out="${args[i+1]}"
  [[ "${args[i]}" != https://* ]] || url="${args[i]}"
done
case "$url" in
  */api/ce/task) printf '{"task":{"status":"SUCCESS","analysisId":"mock-analysis"}}' > "$out";;
  */api/qualitygates/project_status) printf '{"projectStatus":{"status":"ERROR"}}' > "$out";;
  */api/measures/component) printf '{"component":{"measures":[{"metric":"ncloc","value":"1234"},{"metric":"code_smells","value":"7"}]}}' > "$out";;
  *) echo "Unexpected API URL: $url" >&2;exit 7;;
esac
FAKE_CURL
chmod +x "$TEMP/bin/dotnet" "$TEMP/bin/curl" "$TEMP/scanner"
export PATH="$TEMP/bin:$PATH" MOCK_SCANNER_TEMPLATE="$TEMP/scanner"
export MOCK_DOTNET_CALLS="$TEMP/dotnet.log" MOCK_SCANNER_CALLS="$TEMP/scanner.log"
export MOCK_METADATA_POINTER="$TEMP/metadata-pointer.txt"
export SONAR_TOKEN='test-token-do-not-log' SONAR_PROJECT_KEY='sample-project' SONAR_ORGANIZATION='sample-org'
export SONAR_ENFORCE_GATE=false
bash "$ROOT/scripts/code-quality/sonar-cloud.sh" doctor > "$TEMP/doctor.log"
bash "$ROOT/scripts/code-quality/sonar-cloud.sh" setup > "$TEMP/setup.log" 2>&1
grep -q 'PASS: SonarScanner for .NET 11.3.0 is installed.' "$TEMP/setup.log"
bash "$ROOT/scripts/code-quality/sonar-cloud.sh" scan mock-success > "$TEMP/scan.log" 2>&1
[[ "$(grep -c '^build ' "$TEMP/dotnet.log")" == 8 ]]
[[ "$(grep -c '^restore ' "$TEMP/dotnet.log")" == 8 ]]
[[ "$(grep -c '^begin$' "$TEMP/scanner.log")" == 1 ]]
[[ "$(grep -c '^end$' "$TEMP/scanner.log")" == 1 ]]
if grep -q 'test-token-do-not-log' "$TEMP/scan.log"; then echo 'FAIL: token printed' >&2; exit 1; fi
bash "$ROOT/scripts/code-quality/sonar-metrics.sh" mock-success > "$TEMP/metrics.log"
grep -q '| Lines of code | 1234 |' "$TEMP/metrics.log"
grep -q 'Quality gate: \*\*ERROR\*\*' "$TEMP/metrics.log"
# Refuse an incompatible tool rather than continuing with the wrong version.
if MOCK_TOOL_VERSION=10.9.0 bash "$ROOT/scripts/code-quality/sonar-cloud.sh" setup > "$TEMP/wrong-tool.log" 2>&1; then
  echo 'FAIL: setup accepted incorrect SonarScanner version' >&2; exit 1
fi
grep -q 'Expected dotnet-sonarscanner 11.3.0' "$TEMP/wrong-tool.log"
export SONAR_ENFORCE_GATE=true
bash "$ROOT/scripts/code-quality/sonar-cloud.sh" scan mock-enforce > "$TEMP/enforce.log" 2>&1
grep -q '^gate=true$' "$TEMP/scanner.log"
export MOCK_FAIL_BUILD=1
if bash "$ROOT/scripts/code-quality/sonar-cloud.sh" scan mock-build-error > "$TEMP/build-error.log" 2>&1; then
  echo 'FAIL: simulated compile failure was accepted' >&2; exit 1
fi
# No successful scanner upload/end may be reported for a failed build.
[[ ! -f "$ROOT/artifacts/code-quality/sonar/mock-build-error/report-task.txt" ]] || {
  echo 'FAIL: failed build created scan metadata' >&2; exit 1;
}
echo 'PASS: scoped eight-project scanner, token hygiene, quality-gate option, CE polling, metrics evidence and failure propagation.'
echo 'NOTE: This uses mock dotnet/curl; run the real GitHub Actions workflow for genuine results.'
