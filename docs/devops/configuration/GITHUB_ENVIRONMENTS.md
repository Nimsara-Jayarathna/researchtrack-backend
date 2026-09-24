# GitHub Environments, Secrets and Variables

This is the authoritative inventory, derived from `secrets.*` and `vars.*` references in `.github/workflows/*.yml`. It was last verified 2026-09-24.

## Environments

| GitHub Environment | Branch | Entry workflow | Implementation | Target |
|---|---|---|---|---|
| `test` | `develop` (push) or manual | `backend-deploy-test.yml` | `backend-deploy-reusable.yml` | Existing VPS, Docker Compose project `researchtrack-test` |
| `production` | `devops/azure-production-deployment` (push; **temporary, see below**) or manual | `backend-deploy-production.yml` | `backend-deploy-azure-production.yml` | Azure Container Apps + infrastructure VM |
| `production` | `devops/azure-production-deployment` pushes touching infrastructure paths (**temporary**), or manual | `azure-infrastructure.yml` | same file | Azure infrastructure (Bicep) + VM stack |

> **Temporary trigger:** during the Azure migration, both Production workflows run on pushes to `devops/azure-production-deployment` instead of `main`. The target design is `main` (see `../azure-production/DECISIONS.md`). Switch the `on.push.branches` of both workflows back to `main` when the branch is merged. The OIDC federated credential is bound to the `production` Environment, not a branch, so it keeps working. If the `production` Environment has "Deployment branches" restricted to `main`, add this branch as well, or the jobs will be rejected.

`backend-ci.yml` (pull requests) and `backend-build-images.yml` use no environment secrets. Image pushes use the automatic `GITHUB_TOKEN`.

Production **never** falls back to the VPS. `backend-deploy-reusable.yml` is only called by the Test workflow.

## Legend

- **Type:** Secret (GitHub Environment secret) or Variable (GitHub Environment variable). Variables are visible to anyone with repo read access in workflow logs and settings, so they must not hold credentials.
- **Source:**
  - *operator*: chosen by a human
  - *bootstrap*: printed by `deploy/azure/scripts/bootstrap-oidc.sh`
  - *Azure*: derived from Azure or Bicep
  - *external*: issued by GitHub App, Jira, Brevo, etc.
  - *generated*: random value you create
- **Required** means the workflow fails without it. "Default" is the fallback the code applies when the variable is unset.

## Production (`production` Environment)

### Secrets

| Name | Type | Required | Source | Consumed by | Purpose | Notes |
|---|---|---|---|---|---|---|
| `AZURE_CLIENT_ID` | Secret | yes | bootstrap | `azure/login@v2` in both Azure workflows | Client ID of the user-assigned managed identity `id-researchtrack-github-prod` | Not a password; OIDC only, no client secret exists. Kept as a secret by convention |
| `AZURE_TENANT_ID` | Secret | yes | bootstrap | `azure/login@v2` | Entra tenant of the subscription | |
| `AZURE_SUBSCRIPTION_ID` | Secret | yes | bootstrap | `azure/login@v2` | Target subscription | |
| `MYSQL_ENV_FILE` | Secret | yes | operator/generated | infra workflow → VM `/opt/researchtrack/runtime/mysql.env` → MySQL container, DB/user reconciliation, backups; app workflow → validator cross-check + preflight hash check | MySQL root password and the six service DB names/users/passwords | Contract `config/env/mysql/.env.example`. Never sent to Container Apps |
| `SHARED_AUTH_ENV_FILE` | Secret | yes | generated | app workflow → merged into `rt-auth-prod`, `rt-project-prod`, `rt-github-prod` (+ their migration jobs) | JWT issuer/audience/signing key | Contract `shared/.env.example`. Must differ from Test |
| `GATEWAY_ENV_FILE` | Secret | yes | operator | app workflow → `rt-gateway-prod` | Frontend origin, internal service URLs | Contract `gateway/.env.example` |
| `AUTH_ENV_FILE` | Secret | yes | operator/external (Brevo) | → `rt-auth-prod`, `rt-migrate-auth-prod` | Auth DB, registration/password policy, Brevo email, token lifetimes, cookies | Contract `auth/.env.example` |
| `PROJECT_ENV_FILE` | Secret | yes | operator | → `rt-project-prod`, `rt-migrate-project-prod` | Project DB, Auth service URL | Contract `project/.env.example` |
| `RT_GITHUB_SERVICE_ENV` | Secret | yes | operator/external (GitHub App) | → `rt-github-prod`, `rt-migrate-github-prod` | GitHub DB, GitHub App credentials, webhook secret, sync settings | Contract `github/.env.example`. Named differently because GitHub reserves the `GITHUB_` secret prefix |
| `JIRA_ENV_FILE` | Secret | yes | operator/external (Jira) | → `rt-jira-prod`, `rt-migrate-jira-prod` | Jira DB, Jira OAuth | Contract `jira/.env.example` |
| `MEETING_ENV_FILE` | Secret | yes | operator | → `rt-meeting-prod`, `rt-migrate-meeting-prod` | Meeting DB | Contract `meeting/.env.example` |
| `SUBMISSION_ENV_FILE` | Secret | yes | operator | → `rt-submission-prod`, `rt-migrate-submission-prod` | Submission DB, reserved storage keys | Contract `submission/.env.example` |
| `GRAFANA_ENV_FILE` | Secret | yes | operator/generated | infra workflow → VM Grafana container; app workflow validates it | Grafana admin login and public root URL | Contract `grafana/.env.example` |
| `GHCR_PULL_USERNAME` | Secret | only if GHCR packages are private | operator | app workflow → Container App/Job registry credential | GHCR user for image pulls | Used only when **both** pull values are set; otherwise images are pulled anonymously |
| `GHCR_PULL_TOKEN` | Secret | only if GHCR packages are private | operator (GitHub PAT, `read:packages` only) | same | GHCR pull token | Stored as the Container App secret `registry-password` |

The ten `*_ENV_FILE` / `RT_GITHUB_SERVICE_ENV` secrets are **multiline**: one `KEY=value` per line, the full shape of the matching `.env.example`. See [SERVICE_CONFIGURATION.md](SERVICE_CONFIGURATION.md).

### Variables

| Name | Type | Required | Default in code | Source | Consumed by | Purpose |
|---|---|---|---|---|---|---|
| `AZURE_LOCATION` | Variable | **yes** (infra workflow fails without it) | — (the Bicep param file falls back to `southeastasia`, but the workflow never lets it be empty) | operator | `azure-infrastructure.yml` → `production.bicepparam` → resource group and every resource; What-If/deploy location | Azure region. **Choose before the first infra run; a resource group's region cannot change later** |
| `AZURE_VM_ADMIN_SSH_PUBLIC_KEY` | Variable | **yes** | — | operator (`ssh-keygen`) | infra workflow → `production.bicepparam` → VM `authorized_keys` | Required by Azure for Linux VMs. A public key, not a secret. The NSG never opens SSH |
| `AZURE_VM_SIZE` | Variable | no | `Standard_D2as_v5` (in `production.bicepparam`) | operator (quota-driven) | infra workflow exports it only if non-empty; format checked (`^Standard_…`) | Infrastructure VM SKU. This subscription: `Standard_D2as_v4` (see [PRODUCTION_AZURE.md](PRODUCTION_AZURE.md#subscription-specific-values)) |
| `PRODUCTION_API_HOSTNAME` | Variable | no | `api.researchtrack.blipzo.xyz` | operator (DNS) | both Azure workflows → VM `infra.env` → Nginx `server_name` + Let's Encrypt; public endpoint check | Public API hostname |
| `PRODUCTION_GRAFANA_HOSTNAME` | Variable | no | `grafana.researchtrack.blipzo.xyz` | operator (DNS) | both Azure workflows → Nginx + certificate; validator requires `GF_SERVER_ROOT_URL=https://<this>` | Public Grafana hostname |
| `LETSENCRYPT_EMAIL` | Variable | no (recommended) | empty → certbot registers without email | operator | infra workflow → VM `infra.env` → certbot | Certificate expiry notices |
| `ACA_TLS_VERIFY` | Variable | no | `on` | operator | infra workflow → VM `infra.env` → Nginx `proxy_ssl_verify`, Prometheus `insecure_skip_verify`, validation `curl` | Verify the Container Apps certificate on VM → app HTTPS. Set `off` only if verification fails on real Azure (see RUNBOOK) |
| `TEST_HOSTNAMES` | Variable | no | empty (check skipped) | operator | both Azure workflows → `validate-azure-env-files.sh` (`FORBIDDEN_HOSTS`) | Comma-separated Test hostnames; deployment fails if any Production env file mentions one |
| `PRODUCTION_VERIFY_PUBLIC_ENDPOINT` | Variable | no | unset → step skipped | operator | app workflow, final step | `true` after the DNS cutover: GitHub curls `https://<api-host>/health/ready` |
| `AZURE_RESOURCE_GROUP` | Variable | no | `rg-researchtrack-prod` | Azure (Bicep name) | **app workflow only** | Override only if Bicep naming changes. The infra workflow reads the real value from Bicep outputs |
| `AZURE_CONTAINERAPPS_ENVIRONMENT` | Variable | no | `cae-researchtrack-prod` | Azure (Bicep name) | app workflow only | Same as above |
| `AZURE_INFRA_VM` | Variable | no | `vm-researchtrack-infra-prod` | Azure (Bicep name) | app workflow only | Same as above |
| `AZURE_INFRA_PRIVATE_IP` | Variable | no | `10.20.10.4` | Azure (`vmPrivateIp` in `production.bicepparam`) | app workflow → validator `EXPECTED_MYSQL_HOST` | Every DB connection string must use `Server=<this>` |

### Automatic

| Name | Consumed by | Purpose |
|---|---|---|
| `GITHUB_TOKEN` | `backend-build-images.yml` (push to GHCR); app workflow (`docker login` to resolve image digests) | Provided by GitHub Actions |

## Test (`test` Environment)

| Name | Type | Required | Default | Source | Consumed by | Purpose |
|---|---|---|---|---|---|---|
| `SSH_HOST` | Secret | yes | — | operator | `backend-deploy-reusable.yml` | VPS address |
| `SSH_PORT` | Secret | yes (numeric) | — | operator | same | VPS SSH port |
| `SSH_USER` | Secret | yes | — | operator | same | Deploy user on the VPS |
| `SSH_PRIVATE_KEY` | Secret | yes | — | operator | same | Deploy key (never committed) |
| `MYSQL_ENV_FILE` … `GRAFANA_ENV_FILE`, `RT_GITHUB_SERVICE_ENV` | Secret | yes (all ten) | — | operator | same → uploaded as `env/*.env` → Compose services | Same names and contracts as Production, **Test values** |
| `BACKEND_DEPLOY_ROOT` | Variable | yes (absolute path) | — | operator | same | Deploy directory on the VPS, e.g. `/opt/researchtrack/backend`. The stack lands in `<root>/test` |
| `NPM_NET_NAME` | Variable | no | `npm_default` | operator | same → `deploy.env` `EDGE_NETWORK` | External Docker network of the VPS reverse proxy |
| `GRAFANA_HOST_PORT` | Variable | no | `9000` (Compose default) | operator | same → `deploy.env` → `127.0.0.1:<port>:3000` | Loopback-only Grafana port, reached via SSH tunnel |
| `GRAFANA_MEMORY_LIMIT` | Variable | no | `512m` | operator | same → `deploy.env` | Grafana container memory limit |

`SSH_*` may also be stored as **repository** secrets; `secrets.*` resolves Environment secrets first, then repository secrets. They were historically repository secrets shared by Test and the old VPS Production path.

## Shared vs environment-specific

| Category | Names |
|---|---|
| Same name in both Environments, **different values** | `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `GATEWAY_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GRAFANA_ENV_FILE` |
| Test only | `SSH_HOST`, `SSH_PORT`, `SSH_USER`, `SSH_PRIVATE_KEY`, `BACKEND_DEPLOY_ROOT`, `NPM_NET_NAME`, `GRAFANA_HOST_PORT`, `GRAFANA_MEMORY_LIMIT` |
| Production only | everything `AZURE_*`, `PRODUCTION_*`, `LETSENCRYPT_EMAIL`, `ACA_TLS_VERIFY`, `TEST_HOSTNAMES`, `GHCR_PULL_*` |

## Legacy / unused in Production

These are no longer read by any Production workflow. They were used by the old VPS Production path, which `backend-deploy-production.yml` no longer calls. Delete them from the **`production`** Environment once the old VPS Production stack is retired, and keep them in `test`:

`SSH_HOST`, `SSH_PORT`, `SSH_USER`, `SSH_PRIVATE_KEY` (if stored per environment), `BACKEND_DEPLOY_ROOT`, `NPM_NET_NAME`, `GRAFANA_HOST_PORT`, `GRAFANA_MEMORY_LIMIT`.

## Not GitHub configuration (for completeness)

| Value | Where it lives | Set by |
|---|---|---|
| `KAFKA_RETENTION_HOURS` (72), `PROMETHEUS_RETENTION` (`15d`) | VM `/opt/researchtrack/runtime/infra.env`, written by `build-vm-bundle.sh` | Fixed defaults. **Not wired to GitHub variables**; change in `build-vm-bundle.sh` if needed |
| `ACA_ENV_DOMAIN`, `ACA_ENV_STATIC_IP`, `INFRA_PRIVATE_IP`, resource names | Bicep outputs, read by `azure-infrastructure.yml` | Azure |
| Kafka CA, broker certificate, keystore password, cluster ID | VM `/data/researchtrack/kafka/` | Generated on the VM by `reconcile-stack.sh`; never in GitHub |
| `RT_BOOTSTRAP_RESOURCE_GROUP`, `RT_DEPLOY_IDENTITY_NAME` | Local shell when running `bootstrap-oidc.sh` | Optional overrides of `rg-researchtrack-bootstrap` and `id-researchtrack-github-prod` |
