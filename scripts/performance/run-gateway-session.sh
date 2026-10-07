#!/usr/bin/env bash
# Runs inside runtime-session.sh, after the Azure infrastructure is ready.
# Guarantees the Gateway allowance is restored before runtime power cleanup.
set -Eeuo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
gateway="$script_dir/gateway-exemption.sh"
state="${K6_GATEWAY_STATE_FILE:-}"
active="${K6_GATEWAY_ACTIVE_FILE:-}"
enabled="${K6_RATE_LIMIT_BYPASS_ENABLED:-false}"
[[ "$enabled" =~ ^(true|false)$ ]] || { echo 'Invalid gateway exemption switch.' >&2; exit 2; }

restore_gateway() {
  local rc=$? attempt
  trap - EXIT INT TERM
  if [[ "$enabled" == true && -n "$active" && -f "$active" ]]; then
    for ((attempt=1; attempt<=3; attempt++)); do
      echo "Restoring Gateway session (attempt $attempt/3)..." >&2
      if "$gateway" restore "$state"; then
        rm -f "$active"
        break
      fi
      if ((attempt < 3)); then sleep 10; fi
    done
    if [[ -f "$active" ]]; then
      echo 'CRITICAL: Gateway config not restored. Recovery artifact is required.' >&2
      rc=1
    fi
  fi
  exit "$rc"
}
on_signal() { echo "Signal $1: Gateway restoration requested." >&2; exit 130; }
trap restore_gateway EXIT
trap 'on_signal INT' INT
trap 'on_signal TERM' TERM

if [[ "$enabled" == true ]]; then
  [[ -n "$state" && -f "$state" && -n "$active" && -n "${K6_VPS_EGRESS_IP:-}" ]] || {
    echo 'Missing VPS IP, Gateway snapshot or active marker for enabled exemption.' >&2; exit 2;
  }
  umask 077
  : > "$active"  # mark ownership BEFORE ANY gateway mutation
  "$gateway" enable "$state" "$K6_VPS_EGRESS_IP"
else
  echo 'VPS rate-limit exemption disabled by repository variable.'
fi

"$script_dir/run-vps.sh"
