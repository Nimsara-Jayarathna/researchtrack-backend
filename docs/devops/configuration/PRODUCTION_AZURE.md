# Configuring the GitHub `production` Environment (Azure)

Production = `main` → Azure. There are two workflows:

- **`azure-infrastructure.yml`**: Bicep (network, VM, Container Apps environment, private DNS) plus the VM stack (Nginx, MySQL, Kafka, Prometheus, Grafana).
- **`backend-deploy-production.yml`** → **`backend-deploy-azure-production.yml`**: image build, DB migrations, rollout of the seven Container Apps, verification.

Both run in the GitHub Environment **`production`** and authenticate to Azure with OIDC. Architecture: [`../azure-production/`](../azure-production/README.md). Full inventory: [GITHUB_ENVIRONMENTS.md](GITHUB_ENVIRONMENTS.md).

Create it under **Settings → Environments → New environment → `production`**. Restricting deployment branches to `main` and adding required reviewers are recommended but not enforced by the code. The OIDC trust is bound to the environment name, not the branch.

## Authentication model: user-assigned managed identity + OIDC

The SLIIT Entra tenant blocks app registrations (`az ad app create`). GitHub Actions therefore signs in as a **user-assigned managed identity**, created by `deploy/azure/scripts/bootstrap-oidc.sh`:

| Item | Value |
|---|---|
| Resource group | `rg-researchtrack-bootstrap`. Separate from the Bicep-owned `rg-researchtrack-prod`, and never touched by Bicep |
| Identity | `id-researchtrack-github-prod`, in `AZURE_LOCATION` |
| Federated credential | issuer `https://token.actions.githubusercontent.com`, subject `repo:Nimsara-Jayarathna/researchtrack-backend:environment:production`, audience `api://AzureADTokenExchange` |
| Role | Contributor at subscription scope (Bicep creates the production resource group) |
| Client secret | none, ever |

Only a job that declares `environment: production` in this repository can exchange its GitHub token for an Azure token. The workflows request `id-token: write` for this purpose.

```bash
az login
AZURE_LOCATION=<region> ./deploy/azure/scripts/bootstrap-oidc.sh   # idempotent
```

## Environment secrets

| Secret | How to obtain |
|---|---|
| `AZURE_CLIENT_ID` | `az identity show -g rg-researchtrack-bootstrap -n id-researchtrack-github-prod --query clientId -o tsv` |
| `AZURE_TENANT_ID` | `az account show --query tenantId -o tsv` |
| `AZURE_SUBSCRIPTION_ID` | `az account show --query id -o tsv` |
| `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `GATEWAY_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GRAFANA_ENV_FILE` | Start from the matching `config/env/<service>/.env.example`, fill in Production values, and paste the whole file as a multiline secret. See the rules below and [SERVICE_CONFIGURATION.md](SERVICE_CONFIGURATION.md) |
| `GHCR_PULL_USERNAME`, `GHCR_PULL_TOKEN` | Only if the `ghcr.io/<owner>/researchtrack-*` packages are private: a GitHub user plus a classic PAT with `read:packages` only. Check a package's visibility under GitHub → Packages |

Use placeholders like `<azure-client-id>`, `<tenant-id>` and `<subscription-id>` in any notes. Do not paste real IDs into committed files.

### Production rules for the env bundles

`deploy/azure/validation/validate-azure-env-files.sh` enforces these rules before anything is deployed. They differ from Test on purpose.

| Rule | Files |
|---|---|
| Every key of the matching `.env.example` present; no duplicates; no `CHANGE_ME…`/`YOUR_…`/`<…>` placeholders (the reserved `Storage__*` keys are exempt) | all ten |
| `ASPNETCORE_ENVIRONMENT=Production`, `DOTNET_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:8080` | 7 service files |
| `AUTH_SERVICE_URL=http://rt-auth-prod` (likewise `project`, `github`, `jira`, `meeting`, `submission`); Compose names and `localhost` are rejected | `gateway.env` |
| `Services__Auth__BaseUrl=http://rt-auth-prod/` | `project.env` |
| `Services__Project__BaseUrl=http://rt-project-prod/` | `github.env` (`RT_GITHUB_SERVICE_ENV`) |
| `ConnectionStrings__DefaultConnection`: `Server=<AZURE_INFRA_PRIVATE_IP>` (default `10.20.10.4`), `Port=3306`, `SslMode=Required` (or `VerifyCA`/`VerifyFull`), and database/user/password equal to `MYSQL_ENV_FILE` | 6 DB services |
| `FRONTEND_ORIGIN`, `PasswordReset__FrontendBaseUrl`, `GitHub__SetupCallbackUrl`, `GitHub__FrontendReturnOrigin`, `Jira__RedirectUri` are `https://` and not `localhost` | gateway, auth, github, jira |
| `Cookie__Secure=true` | auth |
| `Jwt__SigningKey` at least 32 characters | shared |
| `GitHub__SyncIntervalMinutes` between 1 and 1440 | github |
| `GF_SERVER_ROOT_URL=https://<PRODUCTION_GRAFANA_HOSTNAME>` and a non-empty `GF_SECURITY_ADMIN_PASSWORD` | grafana |
| No value contains a host listed in `TEST_HOSTNAMES` | all |

The `Database__*` keys are only a fallback: the services use `ConnectionStrings__DefaultConnection` when it is set (`DatabaseConnectionStringResolver`). They must still be non-placeholder in Production. Keep them consistent with the connection string: host `10.20.10.4`, port `3306`, SslMode `Required`.

To test the validator locally without real values: `./deploy/azure/validation/test-validate-azure-env-files.sh`. It generates synthetic files from `.env.example`.

## Environment variables

| Variable | Set it? | Value |
|---|---|---|
| `AZURE_LOCATION` | **Required** | The region with quota for the VM size and Container Apps (see below) |
| `AZURE_VM_ADMIN_SSH_PUBLIC_KEY` | **Required** | `ssh-ed25519 AAAA… comment`. Azure requires one; no NSG rule allows SSH |
| `AZURE_VM_SIZE` | This subscription: **yes** | `Standard_B2s`. Unset → Bicep default `Standard_B2s` |
| `PRODUCTION_API_HOSTNAME` | Optional | Default `api.researchtrack.blipzo.xyz` |
| `PRODUCTION_GRAFANA_HOSTNAME` | Optional | Default `grafana.researchtrack.blipzo.xyz` |
| `LETSENCRYPT_EMAIL` | Recommended | Operator mailbox for certificate notices |
| `ACA_TLS_VERIFY` | Optional | Default `on`. See `RUNBOOK.md` → "Container Apps TLS" |
| `TEST_HOSTNAMES` | Recommended | e.g. `api-test.example.com,test.example.com` |
| `PRODUCTION_VERIFY_PUBLIC_ENDPOINT` | After cutover | `true` |
| `AZURE_RESOURCE_GROUP`, `AZURE_CONTAINERAPPS_ENVIRONMENT`, `AZURE_INFRA_VM`, `AZURE_INFRA_PRIVATE_IP` | No | Defaults match the Bicep names and `10.20.10.4`. Set only if you change Bicep; the infrastructure workflow ignores them and reads Bicep outputs |

Do **not** set the Test-only values (`SSH_*`, `BACKEND_DEPLOY_ROOT`, `NPM_NET_NAME`, `GRAFANA_HOST_PORT`, `GRAFANA_MEMORY_LIMIT`) for Production. See [GITHUB_ENVIRONMENTS.md § Legacy](GITHUB_ENVIRONMENTS.md#legacy--unused-in-production).

## Subscription-specific values

These are recorded in `../azure-production/IMPLEMENTATION_STATUS.md` for the current Azure for Students subscription:

| Value | Setting | Why |
|---|---|---|
| OIDC identity | user-assigned managed identity `id-researchtrack-github-prod` | The tenant blocks app registrations |
| `AZURE_VM_SIZE` | `Standard_B2s` | 2 vCPU / 4 GiB burstable B-series, non-Spot. Needs 2 vCPUs of Standard BS Family quota; the VM is not pinned to a zone |
| `AZURE_LOCATION` | **Needs confirmation.** The repository records `southeastasia` (the Bicep fallback, RUNBOOK and STATUS examples, and the quota analysis above), but the region actually in use has been reported as `eastasia` | Whichever region you set, re-check the quota for the chosen VM size **in that region** before the first run |

Quota/SKU checks for a region:

```bash
REGION=<region>
az vm list-usage -l "$REGION" -o table | grep -Ei 'Standard BS Family|Total Regional'
az vm list-skus -l "$REGION" --size Standard_B2s --query '[].{name:name, restrictions:restrictions}' -o json
az provider show -n Microsoft.App --query "resourceTypes[?resourceType=='managedEnvironments'].locations" -o tsv
```

## Values produced by Azure (never set by hand)

The infrastructure workflow reads these from Bicep outputs on every run and passes them on:

| Output | Used for |
|---|---|
| `resourceGroupName`, `vmName`, `containerAppsEnvironmentName` | Run Command targets, network probe |
| `vmPrivateIp` (`10.20.10.4`) | Kafka/MySQL bindings, broker certificate SAN |
| `containerAppsDefaultDomain`, `containerAppsStaticIp` | Nginx upstream `rt-gateway-prod.<domain>`, Prometheus targets, private DNS checks |
| `publicIpAddress` | Shown in the run summary. **You** create the DNS A records from it (see [EXTERNAL_INTEGRATIONS.md](EXTERNAL_INTEGRATIONS.md)) |
