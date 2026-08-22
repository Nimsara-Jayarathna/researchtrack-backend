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
  1. Builds first unless --no-build is supplied.
  2. Checks EF Core migrations for each selected service.
  3. Skips the database update when no migrations are pending.
  4. Applies only pending migrations.

Use --no-build when the solution has already been built,
such as from scripts/start-all.sh.
USAGE
            exit 0
            ;;

        *)
            printf 'Unknown option: %s\n' "$1" >&2
            exit 1
            ;;
    esac
done

if [[ -z "$service" ]]; then
    printf 'Usage: ./scripts/migrate.sh <service|all> [--no-build]\n' >&2
    exit 1
fi

service="${service,,}"
configuration="${CONFIGURATION:-Debug}"

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------

build_for_migration() {
    local requested="$1"
    local project

    if [[ "$skip_build" == true ]]; then
        return 0
    fi

    if [[ "$requested" == "all" ]]; then
        printf 'Building ResearchTrack solution before migration checks...\n\n'

        dotnet build \
            ResearchTrack.sln \
            -c "$configuration" \
            --no-restore

        return 0
    fi

    project="$(rt_service_project "$requested")"

    printf 'Building %s before migration checks...\n\n' "$requested"

    dotnet build \
        "$project" \
        -c "$configuration" \
        --no-restore
}

# ---------------------------------------------------------------------------
# Migration state
#
# Prints:
#   pending
#   none
#
# Returns non-zero ONLY when EF itself fails.
# "No pending migrations" is NOT treated as an error.
# ---------------------------------------------------------------------------

get_migration_state() {
    local svc="$1"
    local project="$2"
    local context="$3"

    local output

    if ! output="$(
        dotnet ef migrations list \
            --project "$project" \
            --startup-project "$project" \
            --context "$context" \
            --configuration "$configuration" \
            --no-build \
            --json \
            2>&1
    )"; then
        printf 'Failed to inspect migrations for %s.\n\n' "$svc" >&2
        printf '%s\n' "$output" >&2
        return 1
    fi

    if grep -Eq \
        '"applied"[[:space:]]*:[[:space:]]*false' \
        <<< "$output"; then

        printf 'pending'
    else
        printf 'none'
    fi

    return 0
}

# ---------------------------------------------------------------------------
# Migrate one service
# ---------------------------------------------------------------------------

migrate_one() {
    local svc="$1"

    local project
    local context
    local state

    # Load service-specific development configuration.
    rt_load_dev_env "$svc"
    rt_validate_db_environment

    project="$(rt_service_project "$svc")"
    context="$(rt_service_context "$svc")"

    printf 'Checking %s (%s)...\n' "$svc" "$context"

    # IMPORTANT:
    # Use the function inside an if condition so set -e does not terminate
    # the script if migration inspection itself fails.
    if ! state="$(get_migration_state "$svc" "$project" "$context")"; then
        printf '  FAILED   Unable to determine migration state.\n'
        return 1
    fi

    case "$state" in
        pending)
            printf '  PENDING  Migration(s) found.\n'
            printf '  APPLY    Applying pending migration(s)...\n'

            dotnet ef database update \
                --project "$project" \
                --startup-project "$project" \
                --context "$context" \
                --configuration "$configuration" \
                --no-build

            printf '  DONE     %s database is up to date.\n' "$svc"
            ;;

        none)
            printf '  SKIP     No pending migrations.\n'
            ;;

        *)
            printf \
                '  FAILED   Unexpected migration state: %s\n' \
                "$state" \
                >&2

            return 1
            ;;
    esac

    return 0
}

# ---------------------------------------------------------------------------
# Execute
# ---------------------------------------------------------------------------

if [[ "$service" == "all" ]]; then

    build_for_migration all

    printf '\n'

    for svc in $(rt_all_db_services); do
        migrate_one "$svc"
        printf '\n'
    done

else

    # Validate the requested service before doing any work.
    rt_service_project "$service" >/dev/null
    rt_service_context "$service" >/dev/null

    build_for_migration "$service"

    printf '\n'

    migrate_one "$service"
fi

printf 'Migration check completed successfully.\n'