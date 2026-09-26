# Azure Production Runbook

Operator steps for the implementation in `deploy/azure/`. The architecture is in `DECISIONS.md` and `MASTER_PLAN.md`, and progress is tracked in `IMPLEMENTATION_STATUS.md`.

## Workflows

| Workflow | Trigger | Does |
|---|---|---|
| `azure-infrastructure.yml` | manual; `main` pushes touching infrastructure paths | Bicep build → OIDC → What-If gate → Bicep deploy (subscription scope) → `configure-vm.sh` → VM stack reconcile → VM/network validation → Container Apps network probe |
| `backend-deploy-production.yml` → `backend-deploy-azure-production.yml` | `main` pushes; manual | build changed images (GHCR, SHA tags) → validate env → OIDC → infrastructure preflight → per changed service: `dbcheck`/`migrate` job → Bicep app deployment → readiness → rollback on failure → full verification |

Both workflows share the concurrency group `researchtrack-azure-production`, so they never overlap. Neither falls back to the VPS.

## 1. One-time manual bootstrap

> The complete secret/variable inventory (sources, consumers, defaults, legacy values) is in [`../configuration/`](../configuration/README.md). The step-by-step first deployment is in [`../configuration/FIRST_DEPLOYMENT_CHECKLIST.md`](../configuration/FIRST_DEPLOYMENT_CHECKLIST.md).

```bash
AZURE_LOCATION=southeastasia ./deploy/azure/scripts/bootstrap-oidc.sh
# optional first argument: <owner>/<repo> (default Nimsara-Jayarathna/researchtrack-backend)
```

The SLIIT Entra tenant blocks app registrations (`az ad app create`), so GitHub Actions authenticates as a **user-assigned managed identity** instead. The script is idempotent. Re-running it only fills in whatever is missing. It creates:

| Item | Value |
|---|---|
| Bootstrap resource group | `rg-researchtrack-bootstrap` in `AZURE_LOCATION`. Kept separate from the Bicep-owned `rg-researchtrack-prod` |
| Managed identity | `id-researchtrack-github-prod` in `AZURE_LOCATION` |
| Federated credential | issuer `https://token.actions.githubusercontent.com`, subject `repo:Nimsara-Jayarathna/researchtrack-backend:environment:production`, audience `api://AzureADTokenExchange` |
| RBAC | Contributor at subscription scope (Bicep creates the production resource group) |

No client secret exists at any point. `AZURE_CLIENT_ID` is the **identity's client ID**. `azure/login` uses it with OIDC exactly as it would an app registration. Only jobs running in the GitHub Environment `production` can obtain a token.

The script ends by printing the values to set on the GitHub Environment `production`.

**Secrets:**
- `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`
- `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `GATEWAY_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GRAFANA_ENV_FILE`. These follow the `config/env/*/.env.example` contracts.
- `GHCR_PULL_USERNAME` and `GHCR_PULL_TOKEN` (`read:packages` only). Needed only if the GHCR packages are private.

**Variables:**

| Variable | Default / example |
|---|---|
| `AZURE_LOCATION` | required, e.g. `southeastasia` |
| `AZURE_VM_ADMIN_SSH_PUBLIC_KEY` | required by Azure for Linux VMs. SSH is never opened in the NSG |
| `AZURE_VM_SIZE` | optional; Bicep default `Standard_B2s` (2 vCPU / 4 GiB, burstable, non-Spot). **This subscription: `Standard_B2s`** (needs Standard BS Family vCPU quota in the region; the VM is deployed without a zone) |
| `PRODUCTION_API_HOSTNAME` | `api.researchtrack.blipzo.xyz` |
| `PRODUCTION_GRAFANA_HOSTNAME` | `grafana.researchtrack.blipzo.xyz` |
| `LETSENCRYPT_EMAIL` | recommended |
| `ACA_TLS_VERIFY` | `on`. See "Container Apps TLS" below |
| `TEST_HOSTNAMES` | Test hostnames that must never appear in Production values |
| `PRODUCTION_VERIFY_PUBLIC_ENDPOINT` | set `true` after the DNS cutover |

Optional variables if you changed the Bicep names or IP: `AZURE_RESOURCE_GROUP`, `AZURE_CONTAINERAPPS_ENVIRONMENT`, `AZURE_INFRA_VM`, `AZURE_INFRA_PRIVATE_IP`.

## 2. Production env values (enforced by `validate-azure-env-files.sh`)

```dotenv
# every service
ASPNETCORE_ENVIRONMENT=Production
DOTNET_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# gateway.env
AUTH_SERVICE_URL=http://rt-auth-prod
PROJECT_SERVICE_URL=http://rt-project-prod
GITHUB_SERVICE_URL=http://rt-github-prod
JIRA_SERVICE_URL=http://rt-jira-prod
MEETING_SERVICE_URL=http://rt-meeting-prod
SUBMISSION_SERVICE_URL=http://rt-submission-prod

# project.env / github.env
Services__Auth__BaseUrl=http://rt-auth-prod/
Services__Project__BaseUrl=http://rt-project-prod/

# every DB service: VM private IP, TLS required
ConnectionStrings__DefaultConnection=Server=10.20.10.4;Port=3306;Database=<db>;User=<user>;Password=<pw>;SslMode=Required

# auth.env
Cookie__Secure=true

# grafana.env
GF_SERVER_ROOT_URL=https://grafana.researchtrack.blipzo.xyz
```

Public URLs (frontend origin, password reset, GitHub callback and return origin, Jira redirect) must be `https://`. Use a JWT signing key that is different from Test's. The S3-shaped `Storage__*` keys may stay as placeholders because Blob integration is deferred.

## 3. First deployment (before any DNS change)

1. Run **Azure Infrastructure - Production** manually. It creates everything, bootstraps the VM, starts the stack, and validates it. Nginx starts in *restricted HTTP mode*: only ACME challenges are public until certificates exist. The run summary shows the public IP.
2. Run **Backend Deploy - Production** manually with `force_full_build: true`. This creates the seven Container Apps (1 replica each), runs every migration job, and verifies:
   - all seven services
   - VM → apps over HTTPS
   - Nginx → Gateway (on-VM)
   - MySQL TLS for all six accounts
   - the Kafka TLS round trip
   - all seven Prometheus targets UP
   - Grafana datasource and dashboards
3. Run **Azure Infrastructure - Production** again. With the apps present it also runs the app-level network checks and the Container Apps → Kafka/MySQL probe.

## 4. Data migration (maintenance window)

On the old VPS, dump the six databases:

```bash
docker compose ... exec -T mysql sh -c 'mysqldump -uroot -p"$MYSQL_ROOT_PASSWORD" --single-transaction --routines --triggers --events --set-gtid-purged=OFF --databases researchtrack_auth researchtrack_project researchtrack_github researchtrack_jira researchtrack_meeting researchtrack_submission' | gzip > prod.sql.gz
```

To restore on the Azure VM:
1. Copy the dump to the VM. There is no public SSH, so use Azure Bastion/Serial Console or a short-lived transfer of your choice.
2. On the VM, run: `gunzip -c prod.sql.gz | /opt/researchtrack/scripts/compose.sh exec -T mysql sh -c 'mysql -uroot -p"$MYSQL_ROOT_PASSWORD"'`.
3. Run the application workflow with `force_redeploy: true` so every `dbcheck`/`migrate` job runs against the restored data.

## 5. Cutover

1. Create DNS A records for the API and Grafana hostnames pointing at the VM public IP.
2. Run the infrastructure workflow. Certbot gets both certificates, and Nginx switches to HTTPS with HTTP→HTTPS redirects. Renewal is handled by the `certbot.timer` system unit, with the `reload-nginx.sh` deploy hook.
3. Update the GitHub App callback and webhook, the Jira OAuth callback and webhook, and any allowlists.
4. Set `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true`, then run the application workflow.
5. Keep the old VPS Production data and host until final acceptance, so a DNS rollback stays possible.

## Operations

| Task | How |
|---|---|
| Grafana | `https://grafana.researchtrack.blipzo.xyz` |
| Shell on the VM | Azure Portal → VM → Run command / Serial console (no public SSH) |
| App logs | `az containerapp logs show -g rg-researchtrack-prod -n rt-<svc>-prod --tail 200` |
| Migration logs | `az containerapp job logs show -g rg-researchtrack-prod -n rt-migrate-<svc>-prod --execution <name> --container <svc>` |
| Roll back an app | `az containerapp revision list -n rt-<svc>-prod -g rg-researchtrack-prod -o table`, then `revision activate` the healthy one and `revision deactivate` the bad one |
| Backups | Daily at 02:30 UTC to `/data/researchtrack/backups`, 7 kept. Run now: `systemctl start researchtrack-mysql-backup` |
| Disk warnings | `journalctl -t researchtrack-disk` (checked every 15 min, warns at 80%) |

## Container Apps TLS

Nginx (`proxy_ssl_verify`) and Prometheus (`insecure_skip_verify`) verify the certificate that the internal Container Apps environment presents for `*.<defaultDomain>` against the public CA bundle. This has **not yet been observed on a real internal environment**.

If the first infrastructure/application run fails with certificate-verification errors on VM → app HTTPS:
1. Set `ACA_TLS_VERIFY=off`. Traffic stays encrypted and VNet-private, but the server certificate is not verified.
2. Record the change in `IMPLEMENTATION_STATUS.md` as a deviation.
