# Azure Production Runbook

Operator steps for the implementation in `deploy/azure/`. The architecture is in `DECISIONS.md` and `MASTER_PLAN.md`, and progress is tracked in `IMPLEMENTATION_STATUS.md`.

## Workflows

| Workflow | Trigger | Does |
|---|---|---|
| `azure-infrastructure.yml` | manual; `devops/sprint4-kafka-infrastructure-verification` pushes touching the existing infrastructure path filters | Bicep build → OIDC → What-If gate → Bicep deploy (subscription scope) → `configure-vm.sh` → VM stack reconcile → VM/network validation → Container Apps network probe |
| `backend-deploy-production.yml` → `backend-deploy-azure-production.yml` | `devops/sprint4-kafka-infrastructure-verification` pushes; manual | build changed images (GHCR, immutable source images) → validate env → OIDC → capture/wake runtime → preflight → per changed service: `dbcheck`/`migrate` job → Bicep app deployment → readiness → rollback on failure → full verification → restore original runtime power states |

Both workflows share the concurrency group `researchtrack-azure-production`, so they never overlap. Neither falls back to the VPS.

## Production branch cutover (2026-10-10)

Automatic Azure production source: `devops/sprint4-kafka-infrastructure-verification`.
The repository's default branch remains `main`. Test still deploys on `develop`;
Backend CI still runs for pull requests targeting `develop` or `main`. The reusable
`backend-deploy-azure-production.yml` has only `workflow_call`; change the push filter
in its caller, not by adding an independent deployment trigger.

**Pushing these reviewed changes to the target branch can immediately start production
deployments.** Application deployment has no path filter. Infrastructure deployment keeps
its existing filters, including its own workflow file, so the initial trigger-change push
can start both workflows. Their shared concurrency serializes them. Preserve `group`,
`cancel-in-progress: false` and `queue: max`, the What-If gate/default destructive-change
refusal, environment/OIDC permissions, migrations, rollback, runtime restoration and
verification. No pipeline or security setting was changed beyond the two push filters.

### Repository-wide cutover requirement

GitHub evaluates push workflows from the pushed ref. Changing files on the target branch
does not change the copies on `main`. As inspected on 2026-10-10, `origin/main` still has
both production push filters set to `main`. Until the owner separately publishes the same
two filter changes on `main`, pushes there can still start the old production workflows.
Updating `main`'s workflow definitions through a reviewed change is needed to stop those
automatic workflow starts repository-wide; it does not require changing the default branch
or merging all target-branch code. No merge, commit, push or remote edit was performed here.

Restricting the `production` Environment to the target branch prevents `main`'s deployment
jobs from accessing that environment, but it does not prevent workflow starts, validation
or image-build jobs that run before the environment gate. Configure that policy before
publishing the switch if the owner wants an immediate deployment boundary.

### GitHub Environment policy and approvals

Read-only GitHub API inspection on 2026-10-10 found:

- Repository default branch `main`; current account has no repository admin permission.
- Environment `production`: `deployment_branch_policy: null` (no branch restriction),
  `protection_rules: []` (no environment protection rules reported).
- Target branch exists at `8890208e9a5e0fec80af30677df83f4b0d2c635d` and reports `protected: false`.

The current target branch is allowed by that unrestricted environment. Referencing an
environment in YAML does not itself require reviewer approval. Existing workflow environment
references remain intact; do not assume a required-reviewer gate exists from those references.
No environment policy, reviewer, wait timer, branch protection or secret was changed.

Owner/admin setup:

1. Open **Settings → Environments → production → Deployment branches and tags**.
2. Choose **Selected branches and tags**, not **Protected branches only**: this target
   currently reports unprotected. Select **Add deployment branch or tag rule**, choose
   **Branch**, and enter the exact name `devops/sprint4-kafka-infrastructure-verification`.
   Avoid a broad wildcard; exact naming also avoids slash-matching ambiguity.
3. To make this the only production source, allow this exact branch. Preserve any other
   deliberately authorized rules until their removal is approved. If a policy was already
   changed since this inspection, review it rather than replacing it blindly.
4. Preserve all existing reviewers, wait timers, administrator-bypass restrictions and
   custom rules. If production approval is required, the owner must configure **Required
   reviewers** where the repository plan supports it; none were reported during inspection.
5. Leave production secrets and repository default branch unchanged. Recheck allowed refs
   and reviewer rules before authorizing any push or manual deployment.

See [GitHub deployment environment policies](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments)
for policy behavior and available protection features.

### OIDC compatibility and live trust check

Both deploying jobs still use `environment: production`, `id-token: write` and
`azure/login@v2`. Environment-based subjects identify the environment rather than the
selected branch, so the branch switch itself does not require a branch-specific credential.
However, live GitHub OIDC configuration reported `use_default: true`,
`use_immutable_subject: true` and this subject prefix:

```text
repo:Nimsara-Jayarathna@66525584/researchtrack-backend@1335622081
```

Combining that observed prefix with the unchanged `production` environment, the expected
subject under the default template is inferred to be:

```text
repo:Nimsara-Jayarathna@66525584/researchtrack-backend@1335622081:environment:production
```

The existing bootstrap script instead describes the legacy name-only subject
`repo:Nimsara-Jayarathna/researchtrack-backend:environment:production`. That source text is
not proof of the live Azure credential. Azure CLI was unavailable here, so the current Azure
issuer/subject/audience/RBAC could not be checked. An immutable-subject mismatch may block
login regardless of branch. Confirm live trust before deployment; do not rerun bootstrap
or create/change security credentials automatically. See [GitHub's OIDC reference](https://docs.github.com/en/actions/reference/security/oidc).

An authorized Azure reader can inspect the existing credential without modifying it:

```bash
az identity federated-credential show \
  --resource-group rg-researchtrack-bootstrap \
  --identity-name id-researchtrack-github-prod \
  --name github-production-environment \
  --query '{issuer:issuer,subject:subject,audiences:audiences}' -o json
```

Expected issuer: `https://token.actions.githubusercontent.com`; audience:
`api://AzureADTokenExchange`; subject must match this repository's actual emitted format
and `production` environment. Also confirm the production identity/client ID and existing
RBAC access. If trust differs, record the difference and obtain owner approval for a
separate Azure security change. No Azure security change is authorized by this branch switch.

### Branch freshness and rollout risks

After read-only fetch on 2026-10-10, the target checkout and remote both point to
`8890208e9a5e0fec80af30677df83f4b0d2c635d`. Compared with `origin/main` at
`1fd42a3`, it has 5 target-only commits and lacks 11 main-only commits. The main-side
changes include expanded unit tests, coverage tooling/thresholds, package/test-project
updates and Backend CI changes. No merge or rebase was attempted.

Before deployment, review this divergence. Deploying a feature branch releases that
branch's source; it can omit newer fixes, tests and CI gates from `main`. Fingerprint
selection correctly follows the selected source but is not a guarantee that this source
is current with `main`; migration/application compatibility remains the owner's review.
Avoid deleting/renaming the deployment branch without updating filters and environment
rules. Restrict who can push to it through an owner-reviewed branch rule. Direct pushes
to this branch do not trigger the unchanged PR-only Backend CI, and production deployment
is not wired to wait for a CI run. Run/review the appropriate checks before pushing.

### Manual deployment and logs

Only after owner authorization, open the repository **Actions** tab:

1. Choose **Azure Infrastructure - Production** or **Backend Deploy - Production**.
2. Select **Run workflow** and choose branch
   `devops/sprint4-kafka-infrastructure-verification` from the branch selector.
3. Keep `allow_destructive`, `force_full_build` and `force_redeploy` at their existing
   false defaults unless a reviewed operation requires them. Dispatch the entrypoint,
   not the reusable `Backend Deploy Azure Production` workflow.
4. Satisfy the configured environment approvals, then inspect the run summary/jobs.

Manual dispatch remains available because both entrypoints retain `workflow_dispatch`
and the workflow files already exist on the default branch. Selecting `main` manually
uses its own workflow version and can be refused by the environment policy.

For infrastructure logs, open the run → **Reconcile Azure Production infrastructure**.
Inspect **What-If**, **Deploy Bicep**, **Configure VM (OS, Docker, data disk)**,
**Reconcile VM stack (Nginx, MySQL, Kafka, Prometheus, Grafana)**, **Validate VM stack**,
and both **Validate private networking** steps. The **Validate Bicep** job runs first.
On Azure Portal, inspect subscription deployment `researchtrack-prod-infrastructure`
and VM Run Command results for the corresponding run; prefer the GitHub step output
and run summary when correlating failures. Do not expose env-file/credential values.

Kafka verification appears in infrastructure **Validate VM stack** and **Validate private
networking (Container Apps side)**, and application **Verify VM stack and monitoring**.
The existing scripts run a bounded producer/consumer round trip against the operational
smoke topic. Look for the named Kafka PASS/FAIL result and its failed step/token/offset
diagnostic. Distinguish topic provisioning, producer, consumer, certificate/SAN and TCP
errors; do not disable TLS checks or restart Kafka as an automatic remedy. A CLI token
round trip does not demonstrate GitHub/Jira application event processing.

Read-only CLI log retrieval for already-existing runs:

```bash
gh run list --repo Nimsara-Jayarathna/researchtrack-backend \
  --workflow azure-infrastructure.yml \
  --branch devops/sprint4-kafka-infrastructure-verification --limit 10
gh run view <run-id> --repo Nimsara-Jayarathna/researchtrack-backend --log-failed
# Use --log instead of --log-failed to inspect successful verification output.
```

These log commands do not dispatch workflows. No live deployment, Kafka restart or
infrastructure recreation was performed while implementing this switch.

### Restore automatic production source to main

After owner review, change only `on.push.branches` back to `["main"]` in
`azure-infrastructure.yml` and `backend-deploy-production.yml`, and update this runbook
and the production branch decision. Keep infrastructure paths, manual inputs, reusable
workflow, permissions, concurrency, approvals and all jobs unchanged.
An admin must allow `main` in the production Environment policy, preserving other
approved rules/security protections. Publish the reviewed trigger edits on every ref
whose production source definition must change; changing only one branch does not rewrite
others. Publishing the restoration may itself trigger production, especially when these
edits reach `main`; coordinate it as a deployment-affecting change. Do not force-push,
merge, modify credentials or run deployment automatically to perform this local task.

### Local validation of this change

- Ruby/Psych parsed every workflow YAML file successfully.
- Semantic comparison with checkout HEAD confirmed that the only workflow changes are
  the two push branch lists: infrastructure paths, manual inputs, permissions, concurrency,
  jobs, reusable calls and verification remain identical.
- Fifteen static event cases passed: target/main/develop push behavior, infrastructure
  path filtering, Test/develop, CI PR targets and both manual-dispatch entrypoints.
- Reusable Azure deployment, Backend CI, Test, image build, OIDC bootstrap and rollout
  scripts were byte-for-byte unchanged. Exactly two workflows and two relevant docs changed;
  nothing was staged. `git diff --check` passed.
- Actionlint 1.7.12 reports the same baseline diagnostics before/after: unsupported
  `concurrency.queue` in the two entrypoints, and ShellCheck SC2129 redirect style in
  the unchanged reusable deployment workflow. It passes with only those known diagnostics
  excluded. `queue: max` is supported by [GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency)
  and was deliberately preserved. No other diagnostics appeared.

These checks evaluate the local workflow definitions, not unmodified remote branch copies.
GitHub policy/OIDC metadata was inspected read-only; live Azure trust could not be checked
because Azure CLI is unavailable. No workflow was dispatched, image published, Azure
resource changed, Kafka restarted or production verification executed by this task.

## 1. One-time manual bootstrap

> The complete secret/variable inventory (sources, consumers, defaults, legacy values) is in [`../configuration/`](../configuration/README.md). The step-by-step first deployment is in [`../configuration/FIRST_DEPLOYMENT_CHECKLIST.md`](../configuration/FIRST_DEPLOYMENT_CHECKLIST.md).

For the already deployed environment, do not bootstrap again to switch branches. Before
any new bootstrap, resolve the immutable-subject compatibility check above: this existing
script encodes the legacy subject, and any Azure security correction requires separate approval.

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

Public URLs (frontend origin, password reset, GitHub callback and return origin, Jira redirect) must be `https://`. Use a JWT signing key that is different from Test's. The Submission Service `Storage__*` keys are active and must point to the private S3 bucket used for research submissions.

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

## Kafka-only operations

Use [KAFKA_OPERATIONS.md](KAFKA_OPERATIONS.md) for read-only `verify-kafka.sh --check`,
explicit smoke writes, topic management and approved independent restart verification.
Do not run the infrastructure workflow merely to check Kafka: it reconciles the whole
stack. Its existing OIDC, production environment, concurrency and What-If gate are preserved.
`KAFKA_RETENTION_HOURS` is an optional production variable (default 72), applied during
the next approved infrastructure reconciliation. See [KAFKA_TASK_REPORT.md](KAFKA_TASK_REPORT.md)
for offline results and outstanding live evidence.
