#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
jmeter_bin="${JMETER_BIN:-jmeter}"
protocol="${PROTOCOL:-http}"
host="${HOST:-localhost}"
port="${PORT:-5000}"
threads="${THREADS:-1}"
rampup="${RAMPUP:-1}"
duration="${DURATION:-60}"
loops="${LOOPS:-1}"
users_file="${USERS_FILE:-$script_dir/data/users.csv}"
think_time_ms="${THINK_TIME_MS:-500}"
think_time_jitter_ms="${THINK_TIME_JITTER_MS:-500}"
timestamp="$(date +%Y%m%d-%H%M%S)"
results_dir="$script_dir/results"
reports_dir="$script_dir/reports"
result_file="$results_dir/researchtrack-$timestamp.jtl"
report_dir="$reports_dir/researchtrack-$timestamp"

if ! command -v "$jmeter_bin" >/dev/null 2>&1; then
  echo "Apache JMeter was not found. Install JMeter 5.6+ or set JMETER_BIN." >&2
  exit 127
fi

if [[ ! -f "$users_file" ]]; then
  echo "User CSV not found: $users_file" >&2
  echo "Copy data/users.csv.example to data/users.csv and add dedicated test credentials." >&2
  exit 2
fi

target_base_url="$protocol://$host:$port"
if ! curl --fail --silent --show-error --max-time 5 "$target_base_url/health/live" >/dev/null; then
  echo "Target health check failed: $target_base_url/health/live" >&2
  echo "Start the ResearchTrack gateway and confirm that endpoint returns HTTP 200 before load testing." >&2
  exit 3
fi

mkdir -p "$results_dir" "$reports_dir"

echo "Target: $target_base_url | users: $threads | ramp-up: ${rampup}s | duration cap: ${duration}s | loops: $loops"
exec "$jmeter_bin" -n \
  -t "$script_dir/test-plan.jmx" \
  -l "$result_file" \
  -e -o "$report_dir" \
  -Jprotocol="$protocol" -Jhost="$host" -Jport="$port" \
  -Jthreads="$threads" -Jrampup="$rampup" -Jduration="$duration" -Jloops="$loops" \
  -Jusers_file="$users_file" \
  -Jthink_time_ms="$think_time_ms" -Jthink_time_jitter_ms="$think_time_jitter_ms"
