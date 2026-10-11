# Azure Production Runbook

Operator steps for the implementation in `deploy/azure/`. The architecture is in `DECISIONS.md` and `MASTER_PLAN.md`, and progress is tracked in `IMPLEMENTATION_STATUS.md`.

## Workflows

| Workflow | Trigger | Does |
|---|---|---|
| `azure-infrastructure.yml` | manual; `integration/main-kafka-production-20261011` pushes touching the existing infrastructure path filters | Bicep build → OIDC → What-If gate → Bicep deploy (subscription scope) → `configure-vm.sh` → VM stack reconcile → VM/network validation → Container Apps network probe |
| `backend-deploy-production.yml` → `backend-deploy-azure-production.yml` | `integration/main-kafka-production-20261011` pushes; manual | required static/.NET/coverage + isolated Kafka/upgrades CI → exact trusted SHA/current branch check → build changed images (GHCR, immutable source images) → recheck SHA → validate env → OIDC → capture/wake runtime → preflight → per changed service: `dbcheck`/`migrate` job → Bicep app deployment → readiness → rollback on failure → full verification → restore original runtime power states |

Both workflows share the concurrency group `researchtrack-azure-production`, so they never overlap. Neither falls back to the VPS.

## Production deployment branch (2026-10-11)

Automatic Azure production source: **`integration/main-kafka-production-20261011`**.
This is the user's current deployment decision and supersedes both the earlier
main-only proposal and the feature-branch deployment policy. The integration branch
serves as the production source without renaming `main` or changing the repository
default branch. Test remains on `develop`. Backend CI runs on integration-branch
pushes through the production workflow’s reusable validation job, and independently on
PRs targeting `develop`, `main` or the integration branch. Existing CI build, coverage,
regression and observability checks are retained; isolated Kafka recovery and real
historical MySQL upgrades are required jobs, not ordinary conditional-test skips.

Both production entrypoints use this exact push branch. Manual deployment and reusable
Azure build/deploy jobs also require
`refs/heads/integration/main-kafka-production-20261011`. The generic reusable
workflow guards production while keeping Test calls available. Other branches and
tags cannot pass these updated production deployment guards. Kafka integration,
mutation, runtime and manual performance workflows can be run with this branch
selected; their manual triggers and inputs are retained. Post-deployment k6 smoke
still follows a successful production deployment when enabled.

### Required same-commit release gate (2026-10-11)

The former independent CI/CD push behavior is corrected locally. The production
entrypoint calls `backend-release-validation.yml`, which runs the shared Backend CI
and isolated Kafka suite before any image builds. Manual production requests run the
same validation. CI has contents-read permission without production secrets/OIDC.
Every release checkout is pinned to the immutable `github.sha`. PR/fork/tag/other-ref
sources are rejected, and the read-only source check requires the approved branch’s
current SHA after CI and again before production credential use. Failed, cancelled,
missing or inconclusive required CI cannot deploy. There is no `workflow_run` or
untrusted artifact handoff. GitHub loads same-repository reusable workflows from the
[caller’s same commit](https://docs.github.com/en/actions/how-tos/reuse-automations/reuse-workflows).

`release-summary` uses only read-only run/job metadata and reports CI, queue/build,
infrastructure, public HTTP and k6 status/durations. The application table records
immutable images, original image-source commits, changed/unchanged applications,
executed migrations and separate migration/rollout/readiness timings. No-op releases
explicitly report no new application version. Disabled public HTTP or k6 checks are
SKIPPED/NOT EXECUTED rather than PASS. CI occupies the existing production concurrency
lock before builds; this adds required recovery-test time while retaining parallel
image builds and preventing overlapping deployment/runtime operations.

A failed k6 smoke fails the workflow. Existing per-app rollout rollback remains
automatic; a cross-service rollback after k6 failure requires the owner-reviewed
revision plan below. Do not roll back additive Kafka migrations or clear outboxes.
Set `K6_AUTO_SMOKE_ENABLED=true` and review `PRODUCTION_VERIFY_PUBLIC_ENDPOINT=true`
for final public release acceptance; neither setting was changed here.

The infrastructure path filters, secret delivery, environment/OIDC, concurrency,
What-If refusal, migrations, image provenance, rollback, runtime restoration and TLS
are unchanged. No commit, push, deployment or remote policy edit was performed.

Current local evidence, exact tests and pending live acceptance are recorded in
[the final Sprint 1–4 audit](../FINAL_DEVOPS_SPRINTS_1_TO_4_AUDIT.md). Earlier branch-policy
validation counts describe the historical change only. No current-task commit, push,
deployment, workflow dispatch or remote settings change was performed.

Historical branch-policy-only verification earlier on 2026-10-11: twelve monitoring/
branch tests, 74 shell syntax checks and diff checks passed. Raw actionlint had the same
ten queue/style findings. That earlier workflow/documentation-only change did not rerun
.NET/Kafka; the later final audit did. The earlier branch-normalization comparison applies
to that snapshot, before the current CI dependency/source-gate changes.

### Repository-wide cutover

GitHub reads push workflow definitions from the pushed ref. Updating this branch
alone cannot stop main's or the old feature branch's workflow copies from deploying.
Before release, an owner must publish the reviewed policy to other previously
deploying branches or restrict production Environment deployment to the selected
integration branch. Do not push any branch as part of this local task. Publishing
integration-branch changes can immediately start application deployment and, when
the existing path filters match, infrastructure reconciliation.

An owner should restrict the `production` Environment to exact **Branch
`integration/main-kafka-production-20261011`**, preserving existing secrets,
reviewers, OIDC and protection settings. Use **Settings → Environments → production
→ Deployment branches and tags → Selected branches and tags**. This restriction
blocks other refs at environment-bound deployment jobs; their old workflow copies
may still start or build images before that gate. Review queued/running deployments
before cutover. Read-only inspection on 2026-10-11 returned
`deployment_branch_policy: null` and `protection_rules: []` for production; no remote
branch restriction or reviewer rule was reported. No environment policy or repository
default branch was changed here.

See [GitHub deployment environment policies](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).

### Source freshness and release order

The selected integration branch combines main fixes with Kafka. Re-fetch and review
subsequent main changes before releasing that source. Changing workflows alone does
not import later application fixes. Preserve all integrated migration, contract,
monitoring and test changes when promoting code.

Shared concurrency serializes application/infrastructure runs but does not establish
infrastructure-first ordering. Verify infrastructure prerequisites before publishing
or coordinate an owner-controlled release window. Preserve `queue: max` and
`cancel-in-progress: false`; do not hold an application run awaiting approval while
it occupies the concurrency group needed by infrastructure.

### OIDC compatibility and live trust check

Both deploying jobs still use `environment: production`, `id-token: write` and
`azure/login@v2`. Environment-based subjects identify the environment rather than the
selected branch, so the branch switch itself does not require a branch-specific credential.
A prior read-only inspection on 2026-10-10 reported GitHub OIDC configuration with `use_default: true`,
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

### Manual deployment and logs

After separate authorization, dispatch **Azure Infrastructure - Production** or
**Backend Deploy - Production** on branch
**`integration/main-kafka-production-20261011`**. Keep destructive inputs false.
Dispatch the entrypoint, never the reusable workflow. Other refs are blocked by the
new guards; old workflow copies still require the environment restriction.

Read-only log retrieval:

```bash
gh run list --repo Nimsara-Jayarathna/researchtrack-backend \
  --workflow backend-deploy-production.yml \
  --branch integration/main-kafka-production-20261011 --limit 10
gh run view <run-id> --repo Nimsara-Jayarathna/researchtrack-backend --log-failed
```

Verify readiness, immutable image provenance, Kafka TLS, metrics, exporter,
Prometheus/Grafana and durable webhook outcomes using `KAFKA_OBSERVABILITY.md`.
Do not restart Kafka, weaken TLS or delete/recreate topics, databases or volumes
as a branch-switch remedy. Keep additive migrations during image rollback.

### Local policy verification

Run `python3 deploy/azure/validation/test_monitoring_reconciliation.py` to validate
selected push branches, manual/reusable guards, Test/develop and monitoring-only safety.
The existing queue syntax and SC2129 style findings remain baseline Actionlint
limitations; no workflow runtime or security body was altered.

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
