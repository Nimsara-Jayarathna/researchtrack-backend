#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PERF_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$PERF_ROOT/../.." && pwd)"
ENV_FILE="${PERF_ENV_FILE:-$PERF_ROOT/config/.env.local}"
SCENARIO="${1:-}"

usage() {
  cat <<USAGE
Usage: $0 <baseline|normal|moderate|higher|stress>

Configuration is loaded from:
  $ENV_FILE

Create it first:
  cp tests/performance/config/.env.example tests/performance/config/.env.local

For the deployed Test environment:
  cp tests/performance/config/test.env.example tests/performance/config/.env.local

Authentication uses either:
  PERF_AUTH_EMAIL + PERF_AUTH_PASSWORD (recommended), or
  PERF_JWT_TOKEN (pre-issued raw access JWT fallback)

Examples:
  $0 baseline
  $0 normal
  PERF_ENV_FILE=/path/to/test.env $0 higher
USAGE
}

[[ -n "$SCENARIO" ]] || { usage; exit 2; }
command -v jmeter >/dev/null 2>&1 || { echo "JMeter is not installed or not on PATH." >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { echo "python3 is required." >&2; exit 1; }
[[ -f "$ENV_FILE" ]] || { echo "Missing performance env file: $ENV_FILE" >&2; usage; exit 1; }

# shellcheck source=../../../scripts/lib/env.sh
source "$REPO_ROOT/scripts/lib/env.sh"
rt_warn_if_insecure_permissions "$ENV_FILE"
rt_load_env_file "$ENV_FILE"
rt_require_env PERF_BASE_URL PERF_PROJECT_ID
rt_reject_placeholder PERF_PROJECT_ID

case "$PERF_PROJECT_ID" in
  ????????-????-????-????-????????????) ;;
  *) echo "PERF_PROJECT_ID must be a GUID." >&2; exit 1 ;;
esac

if [[ "$PERF_BASE_URL" != http://* && "$PERF_BASE_URL" != https://* ]]; then
  echo "PERF_BASE_URL must start with http:// or https://." >&2
  exit 1
fi

read -r PROTOCOL HOST PORT < <(python3 - "$PERF_BASE_URL" <<'PY'
import sys
from urllib.parse import urlparse

url = urlparse(sys.argv[1])
if not url.scheme or not url.hostname or url.username or url.password:
    raise SystemExit("PERF_BASE_URL must be an HTTP(S) origin")
if url.path not in ("", "/") or url.params or url.query or url.fragment:
    raise SystemExit("PERF_BASE_URL must be an origin only, e.g. https://test.api.example.com")
port = url.port or (443 if url.scheme == "https" else 80)
print(url.scheme, url.hostname, port)
PY
)

require_integer() {
  local name="$1" value="$2" minimum="$3"
  if [[ ! "$value" =~ ^[0-9]+$ ]] || (( value < minimum )); then
    echo "$name must be an integer greater than or equal to $minimum." >&2
    exit 1
  fi
}

CONNECT_TIMEOUT="${PERF_CONNECT_TIMEOUT_MS:-10000}"
RESPONSE_TIMEOUT="${PERF_RESPONSE_TIMEOUT_MS:-30000}"
THINK_MIN="${PERF_THINK_TIME_MIN_MS:-2000}"
THINK_MAX="${PERF_THINK_TIME_MAX_MS:-5000}"
STRESS_THINK_MIN="${PERF_STRESS_THINK_TIME_MIN_MS:-250}"
STRESS_THINK_MAX="${PERF_STRESS_THINK_TIME_MAX_MS:-1000}"
TOKEN_MARGIN="${PERF_TOKEN_EXPIRY_MARGIN_SECONDS:-60}"

require_integer PERF_CONNECT_TIMEOUT_MS "$CONNECT_TIMEOUT" 1
require_integer PERF_RESPONSE_TIMEOUT_MS "$RESPONSE_TIMEOUT" 1
require_integer PERF_THINK_TIME_MIN_MS "$THINK_MIN" 0
require_integer PERF_THINK_TIME_MAX_MS "$THINK_MAX" 0
require_integer PERF_STRESS_THINK_TIME_MIN_MS "$STRESS_THINK_MIN" 0
require_integer PERF_STRESS_THINK_TIME_MAX_MS "$STRESS_THINK_MAX" 0
require_integer PERF_TOKEN_EXPIRY_MARGIN_SECONDS "$TOKEN_MARGIN" 0
(( THINK_MAX >= THINK_MIN )) || { echo "PERF_THINK_TIME_MAX_MS must be >= PERF_THINK_TIME_MIN_MS." >&2; exit 1; }
(( STRESS_THINK_MAX >= STRESS_THINK_MIN )) || { echo "PERF_STRESS_THINK_TIME_MAX_MS must be >= PERF_STRESS_THINK_TIME_MIN_MS." >&2; exit 1; }

AUTH_MODE=""
AUTH_EMAIL_B64=""
AUTH_PASSWORD_B64=""
if [[ -n "${PERF_AUTH_EMAIL:-}" || -n "${PERF_AUTH_PASSWORD:-}" ]]; then
  rt_require_env PERF_AUTH_EMAIL PERF_AUTH_PASSWORD
  rt_reject_placeholder PERF_AUTH_EMAIL
  rt_reject_placeholder PERF_AUTH_PASSWORD
  AUTH_MODE="login"
  AUTH_EMAIL_B64="$(python3 -c 'import base64, os; print(base64.b64encode(os.environ["PERF_AUTH_EMAIL"].encode()).decode())')"
  AUTH_PASSWORD_B64="$(python3 -c 'import base64, os; print(base64.b64encode(os.environ["PERF_AUTH_PASSWORD"].encode()).decode())')"
elif [[ -n "${PERF_JWT_TOKEN:-}" ]]; then
  rt_reject_placeholder PERF_JWT_TOKEN
  AUTH_MODE="token"
else
  echo "Configure PERF_AUTH_EMAIL and PERF_AUTH_PASSWORD, or provide PERF_JWT_TOKEN." >&2
  exit 1
fi

PLAN="$PERF_ROOT/ResearchTrack-Performance-Test.jmx"
SUMMARY_SCRIPT="$SCRIPT_DIR/summarize-results.py"
RESULTS_ROOT="$PERF_ROOT/results"
TIMESTAMP="$(date +%Y%m%d-%H%M%S)"
JMETER_PROPERTIES_FILE="$(mktemp "${TMPDIR:-/tmp}/researchtrack-jmeter.XXXXXX")"
chmod 600 "$JMETER_PROPERTIES_FILE"
trap 'rm -f -- "$JMETER_PROPERTIES_FILE"' EXIT

write_properties() {
  local users="$1" rampup="$2" duration="$3" think_min="$4" think_max="$5"
  local required_token_seconds=$((duration + TOKEN_MARGIN))
  local think_range=$((think_max - think_min))

  {
    printf 'protocol=%s\n' "$PROTOCOL"
    printf 'host=%s\n' "$HOST"
    printf 'port=%s\n' "$PORT"
    printf 'project_id=%s\n' "$PERF_PROJECT_ID"
    printf 'users=%s\n' "$users"
    printf 'rampup=%s\n' "$rampup"
    printf 'duration=%s\n' "$duration"
    printf 'connect_timeout=%s\n' "$CONNECT_TIMEOUT"
    printf 'response_timeout=%s\n' "$RESPONSE_TIMEOUT"
    printf 'think_time_min=%s\n' "$think_min"
    printf 'think_time_range=%s\n' "$think_range"
    printf 'required_token_seconds=%s\n' "$required_token_seconds"
    printf 'auth_mode=%s\n' "$AUTH_MODE"
    printf 'auth_email_b64=%s\n' "$AUTH_EMAIL_B64"
    printf 'auth_password_b64=%s\n' "$AUTH_PASSWORD_B64"
    printf 'supplied_jwt=%s\n' "${PERF_JWT_TOKEN:-}"
    printf 'jmeter.save.saveservice.output_format=csv\n'
    printf 'jmeter.save.saveservice.print_field_names=true\n'
    printf 'jmeter.save.saveservice.timestamp_format=ms\n'
    printf 'jmeter.save.saveservice.time=true\n'
    printf 'jmeter.save.saveservice.label=true\n'
    printf 'jmeter.save.saveservice.response_code=true\n'
    printf 'jmeter.save.saveservice.response_message=true\n'
    printf 'jmeter.save.saveservice.successful=true\n'
    printf 'jmeter.save.saveservice.thread_name=true\n'
    printf 'jmeter.save.saveservice.url=true\n'
    printf 'jmeter.save.saveservice.connect_time=true\n'
  } > "$JMETER_PROPERTIES_FILE"
}

run_stage() {
  local label="$1" users="$2" rampup="$3" duration="$4" out="$5" think_min="$6" think_max="$7"
  mkdir -p "$out"
  write_properties "$users" "$rampup" "$duration" "$think_min" "$think_max"

  echo "Running $label: users=$users ramp-up=${rampup}s duration=${duration}s target=$PERF_BASE_URL"
  echo "Think time: ${think_min}-${think_max}ms; authentication mode: $AUTH_MODE"

  jmeter -n -t "$PLAN" -q "$JMETER_PROPERTIES_FILE" \
    -l "$out/results.jtl" -j "$out/jmeter.log" -e -o "$out/report"

  python3 "$SUMMARY_SCRIPT" "$out/results.jtl" "$out" "$label" "$users" "$duration"
  printf 'Scenario: %s\nUsers: %s\nRamp-up: %ss\nDuration: %ss\nThink time: %s-%sms\nTarget: %s\nAuthentication mode: %s\n' \
    "$label" "$users" "$rampup" "$duration" "$think_min" "$think_max" "$PERF_BASE_URL" "$AUTH_MODE" \
    > "$out/run-info.txt"
  echo "Completed $label. HTML report: $out/report/index.html"
}

case "$SCENARIO" in
  baseline)
    run_stage "PERF-001 Baseline" 1 1 120 "$RESULTS_ROOT/PERF-001-$TIMESTAMP" "$THINK_MIN" "$THINK_MAX"
    ;;
  normal)
    run_stage "PERF-002 Normal Usage" 10 30 300 "$RESULTS_ROOT/PERF-002-$TIMESTAMP" "$THINK_MIN" "$THINK_MAX"
    ;;
  moderate)
    run_stage "PERF-003 Moderate Load" 25 60 300 "$RESULTS_ROOT/PERF-003-$TIMESTAMP" "$THINK_MIN" "$THINK_MAX"
    ;;
  higher)
    run_stage "PERF-004 Higher Load" 50 120 600 "$RESULTS_ROOT/PERF-004-$TIMESTAMP" "$THINK_MIN" "$THINK_MAX"
    ;;
  stress)
    BASE="$RESULTS_ROOT/PERF-005-$TIMESTAMP"
    run_stage "PERF-005 Stage 1" 10 30 120 "$BASE/stage-10" "$STRESS_THINK_MIN" "$STRESS_THINK_MAX"
    run_stage "PERF-005 Stage 2" 25 45 120 "$BASE/stage-25" "$STRESS_THINK_MIN" "$STRESS_THINK_MAX"
    run_stage "PERF-005 Stage 3" 50 60 180 "$BASE/stage-50" "$STRESS_THINK_MIN" "$STRESS_THINK_MAX"
    run_stage "PERF-005 Stage 4" 100 120 180 "$BASE/stage-100" "$STRESS_THINK_MIN" "$STRESS_THINK_MAX"
    ;;
  *)
    echo "Unknown scenario: $SCENARIO" >&2
    usage
    exit 2
    ;;
esac
