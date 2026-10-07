#!/usr/bin/env bash
# Offline tests for runtime-readiness.sh. No Azure/Docker/network required.
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
probe="$repo_root/deploy/azure/scripts/runtime-readiness.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/opt/scripts" "$work/opt/runtime"

cat > "$work/opt/runtime/infra.env" <<'ENV'
INFRA_PRIVATE_IP=10.20.10.4
ACA_ENV_DOMAIN=internal.example
ACA_TLS_VERIFY=off
API_HOSTNAME=api.example.test
ENV

cat > "$work/opt/scripts/compose.sh" <<'COMPOSE'
#!/usr/bin/env bash
set -euo pipefail
if [[ "${1:-}" == ps && "${2:-}" == -q ]]; then
  printf 'id-%s\n' "${3:?service}"
  exit 0
fi
if [[ "${1:-}" == exec ]]; then
  exit 0
fi
echo "unexpected compose invocation: $*" >&2
exit 2
COMPOSE
chmod +x "$work/opt/scripts/compose.sh"

cat > "$work/bin/docker" <<'DOCKER'
#!/usr/bin/env bash
set -euo pipefail
id="${@: -1}"
service="${id#id-}"
if [[ "$*" == *'.State.Running'* ]]; then
  echo true
elif [[ "$*" == *'.State.Health'* ]]; then
  if [[ "${MOCK_UNHEALTHY_SERVICE:-}" == "$service" ]]; then echo unhealthy; else echo healthy; fi
else
  echo "unexpected docker invocation: $*" >&2
  exit 2
fi
DOCKER
chmod +x "$work/bin/docker"

cat > "$work/bin/timeout" <<'TIMEOUT'
#!/usr/bin/env bash
# Port checks are the only timeout use in this probe; make them deterministic.
exit "${MOCK_PORT_FAILURE:-0}"
TIMEOUT
chmod +x "$work/bin/timeout"

cat > "$work/bin/curl" <<'CURL'
#!/usr/bin/env bash
set -euo pipefail
if [[ -n "${MOCK_CURL_FAIL_PATTERN:-}" && "$*" == *"$MOCK_CURL_FAIL_PATTERN"* ]]; then
  exit 22
fi
exit 0
CURL
chmod +x "$work/bin/curl"

export PATH="$work/bin:$PATH"
export RT_OPT="$work/opt"
export RUNTIME_READY_ATTEMPTS=2
export RUNTIME_READY_INTERVAL_SECONDS=0.01

run_ok() {
  local name="$1" mode="$2"
  echo "== $name"
  RUNTIME_READINESS_MODE="$mode" "$probe" >/dev/null
  echo "PASS: $name"
}

run_fail() {
  local name="$1" mode="$2"
  echo "== $name"
  if RUNTIME_READINESS_MODE="$mode" "$probe" >/dev/null 2>&1; then
    echo "Expected failure: $name" >&2
    exit 1
  fi
  echo "PASS: $name"
}

unset MOCK_UNHEALTHY_SERVICE MOCK_PORT_FAILURE MOCK_CURL_FAIL_PATTERN || true
run_ok "core VM dependencies ready" infra

export MOCK_UNHEALTHY_SERVICE=mysql
run_fail "unhealthy MySQL blocks acquisition" infra
unset MOCK_UNHEALTHY_SERVICE

run_ok "all application readiness endpoints ready" apps
export MOCK_CURL_FAIL_PATTERN=rt-jira-prod
run_fail "one unready application blocks acquisition" apps
unset MOCK_CURL_FAIL_PATTERN

echo "All runtime readiness validations passed."
