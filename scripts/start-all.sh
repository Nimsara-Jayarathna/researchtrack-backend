#!/usr/bin/env bash
set -euo pipefail

source "$(dirname "$0")/lib/env.sh"
rt_cd_root

usage() {
  cat <<'USAGE'
Usage: ./scripts/start-all.sh [options]

One-command local ResearchTrack startup.

Default workflow:
  1. Verify required local tools.
  2. Create missing service .env.local files via setup.sh.
  3. Validate service ENV/database configuration.
  4. Verify every service development database is reachable.
  5. Build the complete backend and show compiler errors directly.
  6. Apply all pending EF Core migrations.
  7. Restart and launch every backend microservice plus the gateway.
  8. Wait for all readiness checks and print the final health table.

Options:
  --provision          If DB checks fail, run db-init.sh using admin/.env.local,
                       then re-check databases before migrating.
  --skip-migrations    Skip EF Core migration application.
  --no-restart         Do not stop already tracked ResearchTrack processes first.
  --timeout <seconds>  Readiness timeout after startup (default: 60).
  -h, --help           Show this help.

Examples:
  ./scripts/start-all.sh
  ./scripts/start-all.sh --provision
  ./scripts/start-all.sh --timeout 90
USAGE
}

provision=false
skip_migrations=false
restart=true
startup_timeout=60

while [[ $# -gt 0 ]]; do
  case "$1" in
    --provision)
      provision=true
      shift
      ;;
    --skip-migrations)
      skip_migrations=true
      shift
      ;;
    --no-restart)
      restart=false
      shift
      ;;
    --timeout)
      [[ $# -ge 2 ]] || { echo "--timeout requires a value." >&2; exit 1; }
      startup_timeout="$2"
      shift 2
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

if [[ ! "$startup_timeout" =~ ^[0-9]+$ || "$startup_timeout" -lt 1 ]]; then
  echo "--timeout must be a positive number of seconds." >&2
  exit 1
fi

services=(auth project github jira meeting submission gateway)
db_services=(auth project github jira meeting submission)

mkdir -p .run
lock_dir=".run/start-all.lock"
if ! mkdir "$lock_dir" 2>/dev/null; then
  echo "Another start-all operation appears to be running ($lock_dir exists)." >&2
  echo "If no startup is running, remove the stale lock directory and try again." >&2
  exit 1
fi
trap 'rm -rf "$lock_dir"' EXIT HUP INT TERM

section() {
  printf '\n============================================================\n'
  printf '%s\n' "$1"
  printf '============================================================\n'
}

service_base_url() (
  set -euo pipefail
  local service="$1" base_url
  rt_load_dev_env "$service"
  base_url="${ASPNETCORE_URLS%%;*}"
  printf '%s\n' "${base_url%/}"
)

validate_service_envs() {
  local service
  for service in "${services[@]}"; do
    (
      set -euo pipefail
      rt_load_dev_env "$service"
      if [[ "$service" == "gateway" ]]; then
        rt_gateway_env
      else
        rt_validate_db_environment
      fi
    )
    printf '  OK  %s environment\n' "$service"
  done
}

show_failed_logs() {
  local service base_url
  echo
  echo "Recent logs for services that are not ready:"
  for service in "${services[@]}"; do
    base_url="$(service_base_url "$service")"
    if ! curl -fsS --max-time 1 "$base_url/health/ready" >/dev/null 2>&1; then
      printf '\n--- %s (%s) ---\n' "$service" "$base_url"
      if [[ -f ".run/logs/$service.log" ]]; then
        tail -n 60 ".run/logs/$service.log" || true
      else
        echo "No log file found."
      fi
    fi
  done
}

wait_for_readiness() {
  local started_at now elapsed all_ready service base_url
  started_at="$(date +%s)"

  while true; do
    all_ready=true
    for service in "${services[@]}"; do
      base_url="$(service_base_url "$service")"
      if ! curl -fsS --max-time 1 "$base_url/health/ready" >/dev/null 2>&1; then
        all_ready=false
      fi
    done

    if [[ "$all_ready" == true ]]; then
      return 0
    fi

    now="$(date +%s)"
    elapsed=$((now - started_at))
    if (( elapsed >= startup_timeout )); then
      return 1
    fi

    printf 'Waiting for readiness... %ss/%ss\r' "$elapsed" "$startup_timeout"
    sleep 2
  done
}

section "1/8  Preflight"
rt_require_command git
rt_require_command dotnet
rt_require_command mysql
rt_require_command curl

echo "Required commands are available."

missing_env=false
for service in "${services[@]}"; do
  if [[ ! -f "$(rt_env_file "$service")" ]]; then
    missing_env=true
    break
  fi
done

if [[ "$missing_env" == true ]]; then
  echo "One or more service .env.local files are missing; running setup.sh..."
  ./scripts/setup.sh
else
  echo "All service .env.local files already exist."
  # EF CLI is repository-local, so make sure the tool manifest is restored even
  # when first-time setup was completed on an earlier checkout.
  dotnet tool restore
fi

section "2/8  Validate service configuration"
validate_service_envs

section "3/8  Verify MySQL databases"
if ! ./scripts/db-status.sh dev; then
  if [[ "$provision" == true ]]; then
    echo
    echo "Database verification failed; --provision was supplied."
    echo "Running administrator provisioning..."
    ./scripts/db-init.sh
    echo
    echo "Re-checking service database access..."
    ./scripts/db-status.sh dev
  else
    cat >&2 <<'MSG'

Database verification failed.
No privileged database changes were made automatically.

If the databases/users have not been provisioned yet, configure:
  config/env/admin/.env.local

then run either:
  ./scripts/start-all.sh --provision
or:
  ./scripts/db-init.sh
  ./scripts/start-all.sh
MSG
    exit 1
  fi
fi

section "4/8  Build backend"
./scripts/build.sh

section "5/8  Stop tracked backend processes"
if [[ "$restart" == true ]]; then
  ./scripts/stop.sh all
  echo "Tracked ResearchTrack processes stopped (if any were running)."
else
  echo "Skipping stop because --no-restart was supplied."
fi

section "6/8  Apply EF Core migrations"
if [[ "$skip_migrations" == true ]]; then
  echo "Skipping migrations because --skip-migrations was supplied."
else
  ./scripts/migrate.sh all --no-build
fi

section "7/8  Start all backend services"
./scripts/dev.sh all --no-build

section "8/8  Wait for service readiness"
if ! wait_for_readiness; then
  printf '\nServices did not all become ready within %s seconds.\n' "$startup_timeout" >&2
  ./scripts/health.sh all || true
  show_failed_logs
  echo
  echo "Stopping the partially started stack to leave the local environment clean." >&2
  ./scripts/stop.sh all || true
  exit 1
fi

printf '\n'
./scripts/health.sh all

cat <<'DONE'

ResearchTrack backend is ready.

Gateway:
  http://localhost:5000

Swagger (Development/Testing):
  http://localhost:5000/swagger
  http://localhost:5101/swagger
  http://localhost:5102/swagger
  http://localhost:5103/swagger
  http://localhost:5104/swagger
  http://localhost:5105/swagger
  http://localhost:5106/swagger

Logs:
  .run/logs/<service>.log

Stop everything:
  ./scripts/stop.sh all
DONE
