#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/lib/env.sh"
rt_cd_root

usage() {
  echo "Usage: ./scripts/run.sh <gateway|auth|project|github|jira|meeting|submission> [--no-build]"
  echo "       ./scripts/run.sh jmeter"
  echo "       ./scripts/run.sh selenium [pytest-options]"
}

command="${1:-}"
if [[ -z "$command" ]]; then
  usage >&2
  exit 1
fi
shift

case "${command,,}" in
  jmeter)
    if [[ $# -gt 0 ]]; then
      echo "JMeter options are configured with environment variables such as THREADS, RAMPUP, DURATION, and LOOPS." >&2
      usage >&2
      exit 1
    fi
    export PROTOCOL="${PROTOCOL:-https}"
    export HOST="${HOST:-test.api.researchtrack.blipzo.xyz}"
    export PORT="${PORT:-443}"
    export THREADS="${THREADS:-1}"
    export RAMPUP="${RAMPUP:-1}"
    export DURATION="${DURATION:-60}"
    export LOOPS="${LOOPS:-1}"
    exec ./performance-tests/jmeter/run-test.sh
    ;;
  selenium)
    cd tests/selenium
    exec ./run_tests.sh "$@"
    ;;
  -h|--help)
    usage
    exit 0
    ;;
esac

rt_require_command dotnet
service="$command"

no_build=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      no_build=true
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

rt_load_dev_env "$service"
project="$(rt_service_project "$service")"
port="$(rt_service_port "$service")"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://localhost:$port}"

if [[ "${service,,}" == "gateway" ]]; then
  rt_gateway_env
else
  rt_validate_db_environment
fi

echo "Starting $service on $ASPNETCORE_URLS"

run_args=(run --project "$project" --no-launch-profile)
if [[ "$no_build" == true ]]; then
  run_args+=(--no-build)
fi

exec dotnet "${run_args[@]}"
