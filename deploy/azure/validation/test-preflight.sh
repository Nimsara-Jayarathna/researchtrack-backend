#!/usr/bin/env bash
# Offline regression tests for deploy/azure/scripts/preflight.sh.
# Specifically protects the cost-saving production design where a deallocated
# VM is a valid starting state for a deployment.
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
preflight="$repo_root/deploy/azure/scripts/preflight.sh"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin" "$work/env"

cat > "$work/bin/az" <<'AZ'
#!/usr/bin/env bash
set -euo pipefail
state="${MOCK_VM_POWER_STATE:-PowerState/deallocated}"
args=" $* "

if [[ "$args" == *" group show "* ]]; then
  exit 0
elif [[ "$args" == *" network vnet list "* ]]; then
  printf '1\n'
elif [[ "$args" == *" vm show "* && "$args" == *" --query id "* ]]; then
  printf '/subscriptions/sub-test/resourceGroups/rg-researchtrack-prod/providers/Microsoft.Compute/virtualMachines/vm-researchtrack-infra-prod\n'
elif [[ "$args" == *" vm show "* && "$args" == *" --query provisioningState "* ]]; then
  printf 'Succeeded\n'
elif [[ "$args" == *" containerapp env show "* && "$args" == *" properties.provisioningState "* ]]; then
  printf 'Succeeded\n'
elif [[ "$args" == *" containerapp env show "* && "$args" == *" properties.vnetConfiguration.internal "* ]]; then
  printf 'true\n'
elif [[ "$args" == *" containerapp env show "* && "$args" == *" properties.defaultDomain "* ]]; then
  printf 'internal.example\n'
elif [[ "$args" == *" network private-dns zone show "* ]]; then
  exit 0
elif [[ "$args" == *" vm get-instance-view "* ]]; then
  printf '%s\n' "$state"
else
  echo "mock az: unexpected invocation: az $*" >&2
  exit 99
fi
AZ
chmod +x "$work/bin/az"

cat > "$work/bin/sha256sum" <<'SHA'
#!/usr/bin/env bash
# Both local and remote sides use this deterministic value in the mocked test.
printf 'abc123  %s\n' "${1:-file}"
SHA
chmod +x "$work/bin/sha256sum"

# preflight.sh invokes the sibling vm-run.sh by absolute script path. Override
# it temporarily through a copied script tree so production source is untouched.
mkdir -p "$work/scripts"
cp "$preflight" "$work/scripts/preflight.sh"
cat > "$work/scripts/vm-run.sh" <<'VMRUN'
#!/usr/bin/env bash
set -euo pipefail
[[ "${MOCK_VM_RUN_OK:-true}" == true ]]
VMRUN
chmod +x "$work/scripts/"*.sh

printf 'POSTGRES_PASSWORD=test\n' > "$work/env/mysql.env"
export PATH="$work/bin:$PATH"
export RESOURCE_GROUP=rg-researchtrack-prod
export ACA_ENVIRONMENT=cae-researchtrack-prod
export INFRA_VM=vm-researchtrack-infra-prod
export ENV_DIR="$work/env"
export GITHUB_ENV="$work/github-env"

run_test() {
  local name="$1"; shift
  echo "== $name"
  "$@"
  echo "PASS: $name"
}

static_accepts_deallocated() {
  : > "$GITHUB_ENV"
  MOCK_VM_POWER_STATE=PowerState/deallocated "$work/scripts/preflight.sh" static >/dev/null
  grep -qx 'ACA_ENV_DOMAIN=internal.example' "$GITHUB_ENV"
}

static_accepts_stopped() {
  : > "$GITHUB_ENV"
  MOCK_VM_POWER_STATE=PowerState/stopped "$work/scripts/preflight.sh" static >/dev/null
}

static_accepts_running() {
  : > "$GITHUB_ENV"
  MOCK_VM_POWER_STATE=PowerState/running "$work/scripts/preflight.sh" static >/dev/null
}

runtime_rejects_deallocated() {
  if MOCK_VM_POWER_STATE=PowerState/deallocated "$work/scripts/preflight.sh" runtime >/dev/null 2>&1; then
    echo "runtime preflight unexpectedly accepted a deallocated VM" >&2
    exit 1
  fi
}

runtime_accepts_running() {
  MOCK_VM_POWER_STATE=PowerState/running MOCK_VM_RUN_OK=true "$work/scripts/preflight.sh" runtime >/dev/null
}

run_test "static preflight accepts deallocated VM" static_accepts_deallocated
run_test "static preflight accepts stopped VM" static_accepts_stopped
run_test "static preflight accepts running VM" static_accepts_running
run_test "runtime preflight rejects deallocated VM" runtime_rejects_deallocated
run_test "runtime preflight accepts running VM" runtime_accepts_running

echo "All Azure Production preflight validations passed."
