# GitHub Environments, Secrets and Variables

This is the inventory derived from `secrets.*` and `vars.*` references in `.github/workflows/*.yml`. Secret inventory was last verified 2026-09-24; temporary deployment scope and read-only GitHub metadata were reviewed 2026-10-12. See [CI/CD Security Verification](../CICD_SECURITY_VERIFICATION.md) for current evidence and OIDC assumptions.

## Environments

| GitHub Environment | Branch | Entry workflow | Implementation | Target |
|---|---|---|---|---|
| `test` | `develop` (push) or manual | `backend-deploy-test.yml` | `backend-deploy-reusable.yml` | Existing VPS, Docker Compose project `researchtrack-test` |
| `production` | `main` (push) or manual on that branch | `backend-deploy-production.yml` | `backend-deploy-azure-production.yml` | Azure Container Apps + infrastructure VM |
| `production` | `main` pushes touching infrastructure paths, or manual on that branch | `azure-infrastructure.yml` | same file | Azure infrastructure (Bicep) + VM stack |

> `main` is the permanent default branch. Both production deployment workflows deploy from `main`. Manual runtime/performance operations also require `main`. No new integration ruleset or GitHub setting change is required by TASK 02. Expected OIDC trust is Environment-based; actual Azure federation must be inventoried separately. GitHub now reports immutable subject claims, so the older name-only bootstrap subject must not be assumed correct. Remote settings were not changed.

`backend-ci.yml` (reused by production entrypoints; independently on pull requests) and `backend-build-images.yml` use no environment secrets. Image pushes use the automatic `GITHUB_TOKEN`.

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
| `AZURE_VM_SIZE` | Variable | no | `Standard_B2s` (in `production.bicepparam`) | operator (quota-driven) | infra workflow exports it only if non-empty; format checked (`^Standard_…`) | Infrastructure VM SKU. This subscription: `Standard_B2s` (see [PRODUCTION_AZURE.md](PRODUCTION_AZURE.md#subscription-specific-values)) |
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

## Historical owner protection proposal — 2026-10-11

**Superseded scope (2026-10-12):** The proposal below records earlier hardening guidance, not required actions for temporary integration testing. Do not create its integration ruleset or apply its integration-only Environment restriction as part of TASK 02. Main remains the permanent default with its existing protection; current findings and optional future proposals are in [CI/CD Security Verification](../CICD_SECURITY_VERIFICATION.md). The name-only OIDC subject below is historical and differs from current GitHub immutable-subject metadata. No Azure federation change is authorized by this audit.

Read-only GitHub evidence at `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` still shows
integration `protected:false` and production `deployment_branch_policy:null`,
`protection_rules:[]`. Active rulesets target main/develop. No setting was edited.
The application gate is demonstrated in [run 38147705025](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38147705025).
The new infrastructure gate remains local until owner review/publication.

### A. Exact production branch ruleset

1. Open repository **Settings → Rules → Rulesets → New ruleset → New branch ruleset**.
2. Name it `Protect production integration`; set **Enforcement status: Active**.
3. Under **Target branches → Add target → Include by pattern**, enter exactly
   `integration/main-kafka-production-20261011`. Review the matching-branches preview.
4. Enable **Require a pull request before merging**, at least one independent approval,
   dismiss stale approvals and require approval of the latest reviewable push. Enable
   Code Owner review where a maintained CODEOWNERS policy exists; do not invent owners.
5. Enable **Require status checks to pass**, choose GitHub Actions as the expected source,
   and select these actual PR check-run names observed in [CI run 38149500022](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38149500022):
   - `Restore, Build and Test`
   - `Required isolated Kafka recovery and migration upgrades / kafka-integration`
6. Review **Require branches to be up to date before merging**, **Block force pushes**,
   **Restrict deletions**, and required conversation resolution. Use an empty bypass list
   unless a narrowly scoped emergency owner/team is explicitly approved and audited.
7. Save, inspect the effective rule preview, and confirm a normal PR requires both checks.
   Capture settings and successful/blocked PR evidence without bypassing the rules.

Reusable job names include caller prefixes. Do not select a reusable workflow filename or
production deployment check as a PR status requirement; deployment checks do not run on
PRs and would create an impossible approval path. Check names must be copied from the
current PR check-runs again if job names change. The release gate reruns required CI for
the exact release SHA and requires strict success even if GitHub branch rules accept
neutral/skipped conclusions. No merge queue is enabled here; introducing one requires
reviewing the existing PR workflow's `merge_group` support first.

### B. Exact production Environment restriction

1. Open **Settings → Environments → production → Deployment branches and tags**.
2. Choose **Selected branches and tags**. Add a **Branch** rule matching exactly
   `integration/main-kafka-production-20261011`. Remove broader production allow patterns
   only after checking all approved consumers. Add no tag or `refs/pull/*` rule.
3. Under protection rules, configure independent **Required reviewers**, enable
   **Prevent self-review** where supported, and review/disallow administrator bypass where
   supported by the repository plan. Record any plan limitation as unresolved acceptance.
4. Preserve all existing Environment secrets/variables and the Azure OIDC subject
   `repo:Nimsara-Jayarathna/researchtrack-backend:environment:production`; do not recreate
   credentials or replace it with a branch subject.
5. Save and inspect the effective restrictions; retain evidence that main, develop,
   feature branches, tags and PR refs are excluded. Do not dispatch a deployment to test
   the restriction during this local task.

**Protected branches only** is unsuitable for this single approved source: main/develop
also have protection. Use the exact selected Branch rule. Environment rules control jobs
referencing production; they do not secure repository-level SSH secrets or old jobs that
omit the Environment. Inventory those separately before retiring a legacy path.

### C. Legacy paths and safe transition

Workflow definitions are read from each pushed ref. Editing the integration worktree
cannot remove main's or `devops/sprint4-kafka-infrastructure-verification`'s old deployment
copies. First apply the reviewed Environment restriction, audit Azure federated subjects
and privileges and check queued/running releases. Then, through reviewed PRs on each
previously deploying branch, remove/guard only their production trigger while preserving
Test/manual operations. Older builds can still publish mutable tags before an Environment
gate; production must retain immutable source fingerprints/digest/OCI checks. Never rely
on renaming a branch or changing the repository default as a credential boundary.

### D. Optional public verification

Verify the existing Environment variable `PRODUCTION_API_HOSTNAME`, DNS/valid certificate,
Nginx routing and anonymous `GET https://<hostname>/health/ready` contract while runtime is
already available through an approved access context. Expected HTTP 200; redirects, TLS
errors and 4xx/5xx must fail. Readiness may report Degraded with 200 under the existing
policy; this check does not establish healthy Kafka processing. Then set the production
Environment variable **PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true** through the UI. Use the
lowercase value `true`; GitHub's string equality also accepts case variants such as
`True`. Unset/false disables it. No variable was changed here. Follow the read-only
DNS/TLS/response-contract checks and exact operator steps in
[Public API readiness verification](../PUBLIC_API_READINESS_VERIFICATION.md) before enabling it.

The reusable caller's `K6_AUTO_SMOKE_ENABLED` job condition is evaluated before its callee
enters production: keep that flag in repository/organization variable scope if required
there. k6 endpoint thresholds remain unchanged; do not invent a Jira passing target.

Official references: [rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets),
[reusable check naming](https://docs.github.com/en/enterprise-cloud@latest/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/troubleshooting-rules),
[Environment rules and plan availability](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).
