#!/usr/bin/env bash
# Called only on the dedicated VPS by run-vps.sh.
set -Eeuo pipefail
umask 077
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
[[ -f "$root/run.env" ]] || { echo 'Missing temporary environment file.' >&2; exit 2; }
# Even when the GitHub runner disappears, the VPS removes credential material
# as soon as the bounded k6 process exits; only non-secret reports remain.
trap 'rm -f -- "$root/run.env"' EXIT
# run.env was generated using Python shlex.quote and has owner-only permissions.
set -a
# shellcheck disable=SC1091
source "$root/run.env"
set +a
command -v k6 >/dev/null || { echo 'k6 must be installed on the VPS.' >&2; exit 2; }
command -v flock >/dev/null || { echo 'flock is required on the VPS.' >&2; exit 2; }
[[ "${K6_PROFILE:-}" =~ ^(smoke|load|stress|spike|write|lifecycle|webhook)$ ]] || exit 2
mkdir -p "$root/reports"
export K6_REPORT_DIR="$root/reports"
# Additional protection against overlapping ad-hoc VPS runs (GitHub Actions
# already serializes Azure runtime sessions at workflow level).
(
  flock -w 20 9 || { echo 'Another k6 performance test owns this VPS.' >&2; exit 1; }
  timeout --signal=TERM --kill-after=10s 900 \
    k6 run "$root/tests/performance/${K6_PROFILE}.js" \
    2>&1 | tee "$root/reports/${K6_PROFILE}.log"
) 9>"${K6_VPS_LOCK_FILE:-/tmp/researchtrack-k6.lock}"
