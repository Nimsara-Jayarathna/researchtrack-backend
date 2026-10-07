#!/usr/bin/env bash
# Execute temporary work while borrowing the Azure Production runtime.
#
# The session is transactional around power state:
#   capture stable state -> make runtime ready -> execute command -> restore -> verify
#
# Cleanup is attempted both on normal exit and on INT/TERM. GitHub workflows
# should still keep a final `if: always()` restore step as a second line of
# defence because no process-level trap can recover from a runner disappearing.
#
# Usage:
#   runtime-session.sh STATE_FILE -- command [args...]
# Example (current placeholder):
#   runtime-session.sh /tmp/state.json -- sleep 5
set -Eeuo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
POWER="$SCRIPT_DIR/runtime-power.sh"
RESTORE_ATTEMPTS="${RUNTIME_RESTORE_ATTEMPTS:-3}"
RESTORE_RETRY_SECONDS="${RUNTIME_RESTORE_RETRY_SECONDS:-15}"
state_file="${1:-}"
active_file="${RUNTIME_SESSION_ACTIVE_FILE:-${state_file}.active}"
[[ -n "$state_file" ]] || { echo "Usage: $0 STATE_FILE -- command [args...]" >&2; exit 2; }
shift
[[ "${1:-}" == "--" ]] || { echo "Expected '--' before session command." >&2; exit 2; }
shift
(($# > 0)) || { echo "A session command is required." >&2; exit 2; }
[[ "$RESTORE_ATTEMPTS" =~ ^[1-9][0-9]*$ ]] || { echo "RUNTIME_RESTORE_ATTEMPTS must be a positive integer." >&2; exit 2; }

captured=false
cleanup_started=false
cleanup_succeeded=false

restore_with_retries() {
  local attempt
  $captured || return 0
  $cleanup_succeeded && return 0
  $cleanup_started && return 1
  cleanup_started=true

  for ((attempt = 1; attempt <= RESTORE_ATTEMPTS; attempt++)); do
    echo "Restoring captured Azure runtime state (attempt $attempt/$RESTORE_ATTEMPTS) ..."
    if "$POWER" restore "$state_file" && "$POWER" verify "$state_file"; then
      cleanup_succeeded=true
      cleanup_started=false
      rm -f "$active_file"
      echo "Runtime session cleanup verified."
      return 0
    fi
    if ((attempt < RESTORE_ATTEMPTS)); then
      echo "Restore attempt failed; retrying in ${RESTORE_RETRY_SECONDS}s." >&2
      sleep "$RESTORE_RETRY_SECONDS"
    fi
  done

  cleanup_started=false
  echo "CRITICAL: Could not restore the captured Azure runtime state after $RESTORE_ATTEMPTS attempts." >&2
  echo "Keep the snapshot at: $state_file" >&2
  return 1
}

on_exit() {
  local rc=$?
  trap - EXIT INT TERM
  if ! restore_with_retries; then
    # Restoration failure is more important than a successful work command.
    ((rc == 0)) && rc=1
  fi
  exit "$rc"
}

on_signal() {
  local sig="$1"
  echo "Runtime session received $sig; attempting restoration before exit." >&2
  exit 130
}

trap on_exit EXIT
trap 'on_signal INT' INT
trap 'on_signal TERM' TERM

if [[ "${RUNTIME_SESSION_REUSE_SNAPSHOT:-false}" == "true" && -f "$state_file" ]]; then
  echo "Reusing pre-captured runtime snapshot: $state_file"
  "$POWER" validate-snapshot "$state_file"
else
  "$POWER" capture "$state_file"
  "$POWER" validate-snapshot "$state_file"
fi
# Ensure nothing changed between the snapshot and the point at which this
# session takes ownership. If it did, fail without mutating or restoring it.
"$POWER" verify "$state_file"
captured=true
umask 077
: > "$active_file"
"$POWER" start

set +e
"$@"
work_rc=$?
set -e

if ! restore_with_retries; then
  exit 1
fi

trap - EXIT INT TERM
if ((work_rc != 0)); then
  echo "Session command failed with exit code $work_rc; original Azure state was still restored." >&2
fi
exit "$work_rc"
