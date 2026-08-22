#!/usr/bin/env bash

set -euo pipefail

source "$(dirname "$0")/lib/env.sh"

rt_cd_root
rt_require_command dotnet

# ---------------------------------------------------------------------------
# Arguments
# ---------------------------------------------------------------------------

service="${1:-}"

if [[ -n "$service" ]]; then
  shift
fi

skip_build=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-build)
      skip_build=true
      shift
      ;;

    -h|--help)
      cat <<'USAGE'
Usage:
  ./scripts/migrate.sh <service|all> [--no-build]

Examples:
  ./scripts/migrate.sh auth
  ./scripts/migrate.sh project
  ./scripts/migrate.sh all
  ./scripts/migrate.sh all --no-build

Behavior:
  1. Builds the selected project/solution unless --no-build is supplied.
  2. Checks the database for pending EF Core migrations.
  3. Skips services whose migrations are already fully applied.
  4. Runs "dotnet ef database update" only when pending migrations exist.

Use --no-build when the backend has already been successfully built,
for example from scripts/start-all.sh.
USAGE
      exit 0
      ;;

    *)
      echo "Unknown option: $1" >&2
      exit 1
      ;;
  esac
done

if [[ -z "$service" ]]; then
  echo "Usage: ./scripts/migrate.sh <service|all> [--no-build]" >&2
  exit 1
fi

configuration="${CONFIGURATION:-Debug}"

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

build_for_migration() {
  local requested="${1,,}"
  local project

  if [[ "$skip_build" == true ]]; then
    return 0
  fi

  if [[ "$requested" == "all" ]]; then
    echo "Building ResearchTrack solution before migration checks..."

    dotnet build \
      ResearchTrack.sln \
      -c "$configuration" \
      --no-restore

    return 0
  fi

  project="$(rt_service_project "$requested")"

  echo "Building $requested before migration checks..."

  dotnet build \
    "$project" \
    -c "$configuration" \
    --no-restore
}

# ---------------------------------------------------------------------------
# Pending migration check
# ---------------------------------------------------------------------------

has_pending_migrations() {
  local svc="$1"
  local project="$2"
  local context="$3"

  local migration_json
  local status

  set +e

  migration_json="$(
    dotnet ef migrations list \
      --project "$project" \
      --startup-project "$project" \
      --context "$context" \
      --configuration "$configuration" \
      --no-build \
      --json \
      2>&1
  )"

  status=$?

  set -e

  if [[ "$status" -ne 0 ]]; then
    echo "Failed to inspect migrations for $svc." >&2
    echo >&2
    echo "$migration_json" >&2
    return 2
  fi

  if grep -Eq \
    '"applied"[[:space:]]*:[[:space:]]*false' \
    <<< "$migration_json"; then

    return 0
  fi

  return 1
}

# ---------------------------------------------------------------------------
# Migrate one service
# ---------------------------------------------------------------------------

migrate_one() {
  local svc="$1"
  local project
  local context
  local pending_status

  rt_load_dev_env "$svc"
  rt_validate_db_environment

  project="$(rt_service_project "$svc")"
  context="$(rt_service_context "$svc")"

  printf 'Checking %s (%s)...\n' "$svc" "$context"

  set +e

  has_pending_migrations \
    "$svc" \
    "$project" \
    "$context"

  pending_status=$?

  set -e

  case "$pending_status" in
    0)
      printf '  PENDING  Migration(s) found.\n'
      printf '  APPLY    Applying migrations...\n'

      dotnet ef database update \
        --project "$project" \
        --startup-project "$project" \
        --context "$context" \
        --configuration "$configuration" \
        --no-build

      printf '  DONE     %s database is up to date.\n' "$svc"
      ;;

    1)
      printf '  SKIP     No pending migrations.\n'
      ;;

    *)
      printf '  FAILED   Could not determine migration state for %s.\n' \
        "$svc" >&2

      return 1
      ;;
  esac
}

# ---------------------------------------------------------------------------
# Execute
# ---------------------------------------------------------------------------

if [[ "${service,,}" == "all" ]]; then

  build_for_migration all

  echo

  for svc in $(rt_all_db_services); do
    migrate_one "$svc"
    echo
  done

else

  # Validate service name early.
  rt_service_context "$service" >/dev/null
  rt_service_project "$service" >/dev/null

  build_for_migration "$service"

  echo

  migrate_one "${service,,}"
fi