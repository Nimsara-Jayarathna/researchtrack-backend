#!/usr/bin/env bash
# Application-deployment preflight: the Azure infrastructure must already exist
# and be healthy. The application workflow never creates networking resources.
#
# Required environment: RESOURCE_GROUP ACA_ENVIRONMENT INFRA_VM ENV_DIR
set -Eeuo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
not_ready() {
  echo "FAIL: Azure production infrastructure is not ready: $*" >&2
  echo "Run the 'Azure Infrastructure - Production' workflow first." >&2
  exit 1
}

az group show -n "$RESOURCE_GROUP" -o none 2>/dev/null || not_ready "resource group $RESOURCE_GROUP missing"
[[ "$(az network vnet list -g "$RESOURCE_GROUP" --query 'length(@)' -o tsv)" -ge 1 ]] || not_ready "VNet missing"

power="$(az vm get-instance-view -g "$RESOURCE_GROUP" -n "$INFRA_VM" \
  --query "instanceView.statuses[?starts_with(code,'PowerState/')].code | [0]" -o tsv 2>/dev/null || true)"
[[ "$power" == "PowerState/running" ]] || not_ready "VM $INFRA_VM is '${power:-missing}'"

env_state="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.provisioningState -o tsv 2>/dev/null || true)"
[[ "$env_state" == "Succeeded" ]] || not_ready "Container Apps environment is '${env_state:-missing}'"
internal="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.vnetConfiguration.internal -o tsv)"
[[ "$internal" == "true" ]] || not_ready "Container Apps environment is not internal"

domain="$(az containerapp env show -g "$RESOURCE_GROUP" -n "$ACA_ENVIRONMENT" --query properties.defaultDomain -o tsv)"
az network private-dns zone show -g "$RESOURCE_GROUP" -n "$domain" -o none 2>/dev/null || not_ready "private DNS zone $domain missing"

# The VM's MySQL users are reconciled from mysql.env by the infrastructure
# workflow. If the production secret changed since, migrations would fail on
# credentials, so stop early with a clear instruction. Only a hash is compared.
expected="$(sha256sum "$ENV_DIR/mysql.env" | cut -d' ' -f1)"
check="$(mktemp)"
trap 'rm -f "$check"' EXIT
printf '%s\n' '[ "$(sha256sum /opt/researchtrack/runtime/mysql.env | cut -d" " -f1)" = "$EXPECTED_MYSQL_ENV_SHA256" ] || { echo "VM MySQL runtime configuration differs from GitHub production secrets." >&2; exit 1; }; echo "VM runtime configuration matches."' > "$check"
"$script_dir/vm-run.sh" "$RESOURCE_GROUP" "$INFRA_VM" "$check" "EXPECTED_MYSQL_ENV_SHA256=$expected" \
  || not_ready "VM runtime is out of date or Run Command is unavailable"

echo "ACA_ENV_DOMAIN=$domain" >> "${GITHUB_ENV:-/dev/null}"
echo "Preflight passed: resource group, VNet, VM, internal Container Apps environment, private DNS, VM runtime."
