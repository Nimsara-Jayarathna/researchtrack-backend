#!/usr/bin/env bash
# Azure Production deployment preflight.
#
# The production runtime is deliberately allowed to be powered down between
# uses. Therefore deployment preflight is split into two phases:
#
#   preflight.sh static   - prove the long-lived Azure resources exist and are
#                           correctly wired. A Stopped/Deallocated VM is VALID.
#                           Exports ACA_ENV_DOMAIN through GITHUB_ENV.
#
#   preflight.sh runtime  - run only after runtime-power.sh start. Proves the VM
#                           is Running and that its persisted MySQL runtime
#                           configuration matches the current GitHub secret.
#
# Required environment:
#   RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM
# Runtime mode additionally requires ENV_DIR.
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mode="${1:-runtime}"

fail() {
  echo "FAIL: Azure Production preflight ($mode): $*" >&2
  if [[ "$mode" == "static" ]]; then
    echo "Run the 'Azure Infrastructure - Production' workflow if a required Azure resource is missing or misconfigured." >&2
  else
    echo "The deployment workflow should wake Production before runtime preflight. Check the runtime acquisition logs above." >&2
  fi
  exit 1
}

case "$mode" in
  static|runtime) ;;
  *)
    echo "Usage: $0 [static|runtime]" >&2
    exit 2
    ;;
esac

: "${RESOURCE_GROUP:?RESOURCE_GROUP is required}"
: "${ACA_ENVIRONMENT:?ACA_ENVIRONMENT is required}"
: "${INFRA_VM:?INFRA_VM is required}"

# These are control-plane/existence checks and do not require the runtime to be
# powered on.
az group show -n "$RESOURCE_GROUP" -o none 2>/dev/null \
  || fail "resource group $RESOURCE_GROUP is missing"

[[ "$(az network vnet list -g "$RESOURCE_GROUP" --query 'length(@)' -o tsv 2>/dev/null || echo 0)" -ge 1 ]] \
  || fail "VNet is missing"

vm_id="$(az vm show -g "$RESOURCE_GROUP" -n "$INFRA_VM" --query id -o tsv 2>/dev/null || true)"
[[ -n "$vm_id" ]] || fail "VM $INFRA_VM is missing"

vm_provisioning="$(az vm show -g "$RESOURCE_GROUP" -n "$INFRA_VM" --query provisioningState -o tsv 2>/dev/null || true)"
[[ "$vm_provisioning" == "Succeeded" ]] \
  || fail "VM $INFRA_VM provisioning state is '${vm_provisioning:-missing}'"

env_state="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.provisioningState -o tsv 2>/dev/null || true)"
[[ "$env_state" == "Succeeded" ]] \
  || fail "Container Apps environment is '${env_state:-missing}'"

internal="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.vnetConfiguration.internal -o tsv 2>/dev/null || true)"
[[ "$internal" == "true" ]] \
  || fail "Container Apps environment is not internal"

domain="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.defaultDomain -o tsv 2>/dev/null || true)"
[[ -n "$domain" ]] || fail "Container Apps environment default domain is missing"
az network private-dns zone show -g "$RESOURCE_GROUP" -n "$domain" -o none 2>/dev/null \
  || fail "private DNS zone $domain is missing"

# Make the internal Container Apps domain available to later GitHub Actions
# steps. runtime-power.sh needs this when it validates all service
# /health/ready endpoints from inside the VNet.
if [[ -n "${GITHUB_ENV:-}" ]]; then
  echo "ACA_ENV_DOMAIN=$domain" >> "$GITHUB_ENV"
else
  echo "ACA_ENV_DOMAIN=$domain"
fi

if [[ "$mode" == "static" ]]; then
  power="$(az vm get-instance-view -g "$RESOURCE_GROUP" -n "$INFRA_VM" \
    --query "instanceView.statuses[?starts_with(code,'PowerState/')].code | [0]" -o tsv 2>/dev/null || true)"
  case "$power" in
    PowerState/running|PowerState/stopped|PowerState/deallocated)
      echo "Static preflight passed: Azure resources exist; VM current state is $power (valid starting state)."
      ;;
    *)
      # A transient state (starting/deallocating/etc.) is not an infrastructure
      # definition failure. runtime-power.sh capture will wait for it to settle
      # before taking a snapshot.
      echo "Static preflight passed: Azure resources exist; VM is currently ${power:-transitioning/unknown}. Runtime capture will wait for a stable state."
      ;;
  esac
  exit 0
fi

# Runtime checks below intentionally require the VM to be on. They run only
# after the workflow has captured the original state and acquired the runtime.
power="$(az vm get-instance-view -g "$RESOURCE_GROUP" -n "$INFRA_VM" \
  --query "instanceView.statuses[?starts_with(code,'PowerState/')].code | [0]" -o tsv 2>/dev/null || true)"
[[ "$power" == "PowerState/running" ]] \
  || fail "VM $INFRA_VM is '${power:-missing}', expected PowerState/running after runtime acquisition"

: "${ENV_DIR:?ENV_DIR is required for runtime preflight}"
[[ -f "$ENV_DIR/mysql.env" ]] || fail "$ENV_DIR/mysql.env is missing"

# The VM's MySQL users are reconciled from mysql.env by the infrastructure
# workflow. If the production secret changed since then, migrations would fail
# on credentials. Compare hashes without printing secret content.
expected="$(sha256sum "$ENV_DIR/mysql.env" | cut -d' ' -f1)"
check="$(mktemp)"
trap 'rm -f "$check"' EXIT
printf '%s\n' '[ "$(sha256sum /opt/researchtrack/runtime/mysql.env | cut -d" " -f1)" = "$EXPECTED_MYSQL_ENV_SHA256" ] || { echo "VM MySQL runtime configuration differs from GitHub production secrets." >&2; exit 1; }; echo "VM runtime configuration matches."' > "$check"
"$script_dir/vm-run.sh" "$RESOURCE_GROUP" "$INFRA_VM" "$check" "EXPECTED_MYSQL_ENV_SHA256=$expected" \
  || fail "VM runtime is out of date or Run Command is unavailable"

echo "Runtime preflight passed: VM is running and persisted VM runtime configuration matches Production secrets."
