# First Production Deployment Checklist

Work through these in order. Each step links to its details. Command examples use placeholders; never paste real IDs or secrets into committed files.

## 1. Azure login and provider registration
- [ ] `az login`, then confirm the subscription: `az account show --query "{name:name, id:id, tenant:tenantId}" -o table`
- [ ] Register providers. `bootstrap-oidc.sh` also does this; registration can take minutes:
  `for p in Microsoft.App Microsoft.OperationalInsights Microsoft.Network Microsoft.Compute Microsoft.ManagedIdentity; do az provider register -n $p; done`

## 2. Region and quota (student subscription)
- [ ] Pick `AZURE_LOCATION`. **Confirm the region**: the repository records `southeastasia`, but `eastasia` has been reported as the region in use ([PRODUCTION_AZURE.md](PRODUCTION_AZURE.md#subscription-specific-values)).
- [ ] Check that the VM family has quota in that region: `az vm list-usage -l <region> -o table | grep -Ei 'DASv4|DASv5'`
- [ ] Check that the SKU is not fully restricted: `az vm list-skus -l <region> --size Standard_D2as_v4 --query '[].restrictions'`. A zone-only restriction is fine because no zone is pinned.
- [ ] Check that Container Apps is offered in the region: `az provider show -n Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations"`
- [ ] Decide `AZURE_VM_SIZE`. This subscription uses `Standard_D2as_v4`; unset means `Standard_D2as_v5`.

## 3. Managed identity / OIDC bootstrap
- [ ] `AZURE_LOCATION=<region> ./deploy/azure/scripts/bootstrap-oidc.sh`. It creates `rg-researchtrack-bootstrap`, `id-researchtrack-github-prod`, the federated credential for `environment:production`, and Contributor at subscription scope. It is idempotent.
- [ ] Check the credential: `az identity federated-credential list -g rg-researchtrack-bootstrap --identity-name id-researchtrack-github-prod -o table`

## 4. GitHub `production` secrets ([PRODUCTION_AZURE.md](PRODUCTION_AZURE.md#environment-secrets))
- [ ] Create the Environment `production` (and restrict it to `main`)
- [ ] `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- [ ] `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `GATEWAY_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GRAFANA_ENV_FILE`, with Production rules ([SERVICE_CONFIGURATION.md](SERVICE_CONFIGURATION.md))
- [ ] JWT signing key, DB passwords and Grafana password are **different from Test**
- [ ] If the GHCR packages are private: `GHCR_PULL_USERNAME`, `GHCR_PULL_TOKEN` (`read:packages`)

## 5. GitHub `production` variables
- [ ] `AZURE_LOCATION`, `AZURE_VM_ADMIN_SSH_PUBLIC_KEY` (required)
- [ ] `AZURE_VM_SIZE=Standard_D2as_v4` (this subscription)
- [ ] `LETSENCRYPT_EMAIL`, `TEST_HOSTNAMES`
- [ ] Optional: `PRODUCTION_API_HOSTNAME`, `PRODUCTION_GRAFANA_HOSTNAME`, `ACA_TLS_VERIFY` (defaults are fine)
- [ ] Do **not** set `PRODUCTION_VERIFY_PUBLIC_ENDPOINT` yet
- [ ] Optional sanity check before pushing: `./deploy/azure/validation/test-validate-azure-env-files.sh` (synthetic values only)

## 6. Infrastructure workflow
- [ ] Actions → **Azure Infrastructure - Production** → Run workflow
- [ ] The What-If summary shows only creates on the first run
- [ ] VM configure, stack reconcile and validation steps are green. Nginx is in restricted HTTP mode (expected before DNS).
- [ ] Note the **public IP** from the run summary

## 7. Application workflow
- [ ] Actions → **Backend Deploy - Production** → Run workflow with `force_full_build: true`
- [ ] The preflight passes, and all six migration jobs succeed
- [ ] All seven Container Apps become healthy (1 replica each)
- [ ] "Verify VM stack and monitoring" and "Verify private networking" are green (7 Prometheus targets UP, Grafana dashboards present)
- [ ] Re-run **Azure Infrastructure - Production**: with the apps present it runs the app-level network checks and the Container Apps → MySQL/Kafka probe
- [ ] If VM → app HTTPS fails certificate verification, follow RUNBOOK → "Container Apps TLS" (`ACA_TLS_VERIFY`)

## 8. Database migration
- [ ] Maintenance window: dump the old Production databases and restore them on the VM ([RUNBOOK §4](../azure-production/RUNBOOK.md#4-data-migration-maintenance-window))
- [ ] Re-run the application workflow with `force_redeploy: true` (dbcheck/migrate against the restored data)
- [ ] Spot-check row counts and key user flows

## 9. DNS
- [ ] A records: `api.researchtrack.blipzo.xyz` and `grafana.researchtrack.blipzo.xyz` → VM public IP ([EXTERNAL_INTEGRATIONS.md](EXTERNAL_INTEGRATIONS.md#only-during-the-production-cutover))
- [ ] `dig +short api.researchtrack.blipzo.xyz` returns the VM IP

## 10. TLS
- [ ] Re-run **Azure Infrastructure - Production**. Certificates are issued and Nginx switches to HTTPS.
- [ ] `https://api.researchtrack.blipzo.xyz/health/ready` returns 200; `http://` redirects to `https://`
- [ ] `https://grafana.researchtrack.blipzo.xyz` shows the Grafana login

## 11. GitHub / Jira callback cutover
- [ ] GitHub App: Setup/Callback URL and Webhook URL on the Production API hostname; webhook secret equals `GitHub__WebhookSecret`
- [ ] Jira OAuth callback/webhook updated. **Confirm the paths from the merged Jira code first**
- [ ] External allowlists updated to the VM public IP

## 12. Final production validation
- [ ] Set `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true` and run **Backend Deploy - Production**; every step is green
- [ ] Registration/login, project access, GitHub linking and webhook delivery, Jira connection
- [ ] Rate limiting sees real client IPs (Gateway logs show distinct client addresses)
- [ ] Cookies are `Secure` over HTTPS; CORS works from the frontend origin
- [ ] Kafka smoke test and Prometheus/Grafana are green (in the workflow verification)
- [ ] Update [`IMPLEMENTATION_STATUS.md`](../azure-production/IMPLEMENTATION_STATUS.md) with what was proven
- [ ] After acceptance, remove the legacy VPS-only values from the `production` Environment ([GITHUB_ENVIRONMENTS.md](GITHUB_ENVIRONMENTS.md#legacy--unused-in-production)) and retire the old VPS Production stack. Test stays.
