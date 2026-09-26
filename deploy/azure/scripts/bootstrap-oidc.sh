#!/usr/bin/env bash
# Manual, one-time Azure <-> GitHub trust bootstrap (docs/devops/azure-production/
# 03_IAC_AND_BOOTSTRAP.md). Creates NO Production infrastructure: everything
# else is Bicep, applied by .github/workflows/azure-infrastructure.yml.
#
# Uses a user-assigned managed identity instead of an Entra app registration,
# because the tenant blocks `az ad app create`. Only Azure RBAC rights on the
# subscription are needed. Idempotent: safe to re-run.
#
#   1. bootstrap resource group      rg-researchtrack-bootstrap
#   2. user-assigned identity        id-researchtrack-github-prod
#   3. GitHub OIDC federated credential for the `production` Environment
#      (no client secret exists at any point)
#   4. Contributor at subscription scope (Bicep creates rg-researchtrack-prod)
#   5. prints the GitHub Environment values to set
#
# Usage:
#   AZURE_LOCATION=southeastasia ./deploy/azure/scripts/bootstrap-oidc.sh [owner/repo]
set -euo pipefail

repo="${1:-Nimsara-Jayarathna/researchtrack-backend}"
location="${AZURE_LOCATION:?AZURE_LOCATION is required (region for the bootstrap resource group and identity)}"
resource_group="${RT_BOOTSTRAP_RESOURCE_GROUP:-rg-researchtrack-bootstrap}"
identity_name="${RT_DEPLOY_IDENTITY_NAME:-id-researchtrack-github-prod}"
credential_name="github-production-environment"
subject="repo:$repo:environment:production"
issuer="https://token.actions.githubusercontent.com"
audience="api://AzureADTokenExchange"

subscription_id="$(az account show --query id -o tsv)"
tenant_id="$(az account show --query tenantId -o tsv)"
echo "Subscription $subscription_id (tenant $tenant_id), region $location"

echo "[1/4] Resource group $resource_group"
if [[ "$(az group exists -n "$resource_group")" != true ]]; then
  az group create -n "$resource_group" -l "$location" -o none
fi

echo "[2/4] Managed identity $identity_name"
if ! az identity show -g "$resource_group" -n "$identity_name" -o none 2>/dev/null; then
  az identity create -g "$resource_group" -n "$identity_name" -l "$location" -o none
fi
client_id="$(az identity show -g "$resource_group" -n "$identity_name" --query clientId -o tsv)"
principal_id="$(az identity show -g "$resource_group" -n "$identity_name" --query principalId -o tsv)"

echo "[3/4] Federated credential $subject"
existing_subject="$(az identity federated-credential show -g "$resource_group" \
  --identity-name "$identity_name" -n "$credential_name" --query subject -o tsv 2>/dev/null || true)"
if [[ -z "$existing_subject" ]]; then
  az identity federated-credential create -g "$resource_group" \
    --identity-name "$identity_name" -n "$credential_name" \
    --issuer "$issuer" --subject "$subject" --audiences "$audience" -o none
elif [[ "$existing_subject" != "$subject" ]]; then
  echo "Federated credential $credential_name exists with subject '$existing_subject', expected '$subject'." >&2
  echo "Delete it or set RT_DEPLOY_IDENTITY_NAME to a different identity." >&2
  exit 1
fi

echo "[4/4] Contributor on /subscriptions/$subscription_id"
scope="/subscriptions/$subscription_id"
if ! az role assignment list --assignee "$principal_id" --scope "$scope" --role Contributor \
    --query '[0].id' -o tsv 2>/dev/null | grep -q .; then
  # A new identity can take a minute to replicate before it can be assigned.
  for attempt in 1 2 3 4 5 6; do
    if az role assignment create --assignee-object-id "$principal_id" \
        --assignee-principal-type ServicePrincipal --role Contributor --scope "$scope" -o none; then
      break
    fi
    ((attempt < 6)) || { echo "Role assignment failed." >&2; exit 1; }
    echo "      waiting for identity replication..."
    sleep 20
  done
fi

for provider in Microsoft.App Microsoft.OperationalInsights Microsoft.Network Microsoft.Compute; do
  az provider register --namespace "$provider" -o none
done

cat <<EOF

Bootstrap complete (no client secret). Set on GitHub Environment "production" ($repo):
  secret   AZURE_CLIENT_ID=$client_id
  secret   AZURE_TENANT_ID=$tenant_id
  secret   AZURE_SUBSCRIPTION_ID=$subscription_id
  variable AZURE_LOCATION=$location
  variable AZURE_VM_ADMIN_SSH_PUBLIC_KEY=<an SSH public key; SSH is never opened in the NSG>
EOF
