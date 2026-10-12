#!/usr/bin/env bash
# Obtain the exact SonarQube Cloud analysis submitted by the .NET scanner.
# Requires jq/curl. Never writes credentials into the evidence artifacts.
set -euo pipefail
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
RUN="${1:-local}"
[[ "$RUN" =~ ^[a-zA-Z0-9][a-zA-Z0-9._-]{0,55}$ ]] || { echo 'Invalid run identifier' >&2; exit 2; }
for bin in jq curl; do
  command -v "$bin" >/dev/null || { echo "ERROR: $bin is required for API reporting" >&2; exit 2; }
done
[[ -n "${SONAR_TOKEN:-}" ]] || { echo 'ERROR: SONAR_TOKEN is missing' >&2; exit 2; }
REPORT="$ROOT/artifacts/code-quality/sonar/$RUN"
mkdir -p "$REPORT"
TASKFILE="$REPORT/report-task.txt"
if [[ ! -s "$TASKFILE" && -s "$ROOT/.sonarqube/out/.sonar/report-task.txt" ]]; then
  cp "$ROOT/.sonarqube/out/.sonar/report-task.txt" "$TASKFILE"
fi
[[ -s "$TASKFILE" ]] || { echo 'ERROR: scanner report-task.txt is missing (no upload evidence)' >&2; exit 1; }
TASK_ID="$(sed -n 's/^ceTaskId=//p' "$TASKFILE" | head -1 | tr -d '\r')"
[[ -n "$TASK_ID" ]] || { echo 'ERROR: missing ceTaskId in report-task.txt' >&2; exit 1; }

case "${SONAR_REGION:-eu}" in
  ''|eu) HOST='https://sonarcloud.io' ;;
  us) HOST='https://sonarqube.us' ;;
  *) echo 'ERROR: invalid SONAR_REGION' >&2; exit 2 ;;
esac
AUTH="Authorization: Bearer ${SONAR_TOKEN}"

# Wait for this submitted CE task, not just a potentially stale project-wide status.
STATUS=''
for attempt in $(seq 1 40); do
  curl --fail --silent --show-error --retry 2 -H "$AUTH" --get \
    --data-urlencode "id=$TASK_ID" "$HOST/api/ce/task" -o "$REPORT/ce-task.json"
  STATUS="$(jq -r '.task.status // empty' "$REPORT/ce-task.json")"
  case "$STATUS" in
    SUCCESS) break ;;
    FAILED|CANCELED) echo "ERROR: cloud processing status is $STATUS" >&2; exit 1 ;;
    PENDING|IN_PROGRESS) sleep 10 ;;
    *) echo "ERROR: unexpected compute-engine status: $STATUS" >&2; exit 1 ;;
  esac
done
[[ "$STATUS" == SUCCESS ]] || { echo 'ERROR: analysis not processed within 400 seconds' >&2; exit 1; }
ANALYSIS_ID="$(jq -r '.task.analysisId // empty' "$REPORT/ce-task.json")"
[[ -n "$ANALYSIS_ID" ]] || { echo 'ERROR: processed analysis has no analysisId' >&2; exit 1; }

curl --fail --silent --show-error --retry 2 -H "$AUTH" --get \
  --data-urlencode "analysisId=$ANALYSIS_ID" \
  "$HOST/api/qualitygates/project_status" -o "$REPORT/quality-gate.json"
GATE="$(jq -r '.projectStatus.status // "UNKNOWN"' "$REPORT/quality-gate.json")"

# Stable, widely supported standard measures. Unavailable metrics are displayed
# as 'not reported', never misrepresented as zeros.
PROJECT="${SONAR_PROJECT_KEY:-$(sed -n 's/^projectKey=//p' "$TASKFILE" | head -1)}"
[[ -n "$PROJECT" ]] || { echo 'ERROR: project key missing from metadata and environment' >&2; exit 1; }
curl --fail --silent --show-error --retry 2 -H "$AUTH" --get \
  --data-urlencode "component=$PROJECT" \
  --data-urlencode 'metricKeys=ncloc,complexity,cognitive_complexity,duplicated_lines_density,code_smells,sqale_index,coverage,new_coverage' \
  "$HOST/api/measures/component" -o "$REPORT/measures.json"

summary="$REPORT/sonarqube-summary.md"
{
  echo '# ResearchTrack — SonarQube Cloud genuine analysis evidence'
  echo
  printf 'Project key: `%s`  \n' "$PROJECT"
  printf 'Analysis ID: `%s`  \n' "$ANALYSIS_ID"
  printf 'Compute engine: `%s`  \n' "$STATUS"
  printf 'Quality gate: **%s**  \n' "$GATE"
  echo 'Scanner: SonarScanner for .NET 11.3.0, eight production project builds.'
  echo
  echo '| Indicator | Measured value |'
  echo '|---|---:|'
  for item in \
    'ncloc:Lines of code' \
    'complexity:Cyclomatic complexity' \
    'cognitive_complexity:Cognitive complexity' \
    'duplicated_lines_density:Duplicated lines (%)' \
    'code_smells:Code smells' \
    'sqale_index:Technical debt (minutes)' \
    'coverage:Overall test coverage (%)' \
    'new_coverage:Coverage on new code (%)'; do
    key="${item%%:*}" label="${item#*:}"
    value="$(jq -r --arg k "$key" '[.component.measures[]? | select(.metric == $k) | (.value // .period.value // .periods[0].value)] | first // empty' "$REPORT/measures.json")"
    # For new-code values, the quality-gate condition is the authoritative
    # fallback when the measures API represents a metric only as a period.
    if [[ -z "$value" && "$key" == new_coverage ]]; then
      value="$(jq -r '[.projectStatus.conditions[]? | select(.metricKey == "new_coverage") | .actualValue] | first // empty' "$REPORT/quality-gate.json")"
    fi
    printf '| %s | %s |\n' "$label" "${value:-not reported}"
  done
  echo
  echo 'Quality-gate enforcement is controlled by the GitHub Actions variable `SONAR_ENFORCE_GATE`.'
  echo 'This evidence is from the finished cloud analysis, not locally estimated metrics.'
  echo 'Raw JSON files: `ce-task.json`, `quality-gate.json`, `measures.json`.'
  if [[ -s "$REPORT/coverage-files.txt" ]]; then
    printf 'Coverage reports produced: **%d** (existing unit-test suites).\n' "$(wc -l < "$REPORT/coverage-files.txt")"
    echo 'See `coverage-collection-summary.md` and `coverage/` for OpenCover evidence.'
  fi
  echo
  echo '### Quality gate conditions (actual analysis)'
  echo
  echo '| Condition | Status | Actual | Threshold |'
  echo '|---|---|---:|---:|'
  jq -r '.projectStatus.conditions[]? | "| \(.metricKey) | \(.status) | \(.actualValue // "not reported") | \(.errorThreshold // "not set") |"' "$REPORT/quality-gate.json"
  echo
  printf 'Small-change conditions ignored: `%s`\n' "$(jq -r '.projectStatus.ignoredConditions // false' "$REPORT/quality-gate.json")"
} > "$summary"
if [[ -n "${GITHUB_STEP_SUMMARY:-}" ]]; then cat "$summary" >> "$GITHUB_STEP_SUMMARY"; fi
cat "$summary"
# Gate enforcement is handled by the SonarScanner end step when enabled. This
# reporting script reports a red gate without masking the original scan result.
