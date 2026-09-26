# Azure Production Implementation Status

Update this file after each meaningful implementation session.

This is intentionally short so the next agent does not re-audit everything.

Last updated: 2026-09-24. **Code complete, not yet executed against Azure.**

## Current phase

- [x] Phase 0 — guardrails/docs
- [ ] Phase 1 — manual OIDC/RBAC bootstrap (`deploy/azure/scripts/bootstrap-oidc.sh`: user-assigned managed identity; working configuration exists, see below)
- [x] Phase 2 — Bicep foundation (code; not deployed)
- [x] Phase 3 — VM bootstrap (code; not run)
- [x] Phase 4 — VM stack (code; not run)
- [x] Phase 5 — Container Apps (code; not deployed)
- [x] Phase 6 — private networking (validation code; not run)
- [x] Phase 7 — production app deployment (code; not run)
- [x] Phase 8 — monitoring (code; not run)
- [ ] Phase 9 — Kafka app integration (out of scope; broker + TLS smoke test only)
- [ ] Phase 10 — public cutover (runbook written; needs DNS/callbacks)

"Code" means it is implemented and statically validated only. Nothing has touched Azure: no `az` login was available.

## Completed

- **Bicep** (`deploy/azure/main.bicep`, subscription scope, plus `modules/*`, `parameters/production.bicepparam`) creates:
  - RG, VNet `10.20.0.0/16`
  - infra subnet `10.20.10.0/24` with NSG: 80/443 public only, 3306/9092 from the Container Apps subnet only, no SSH rule
  - Container Apps subnet `10.20.20.0/23` (delegated)
  - static public IP, NIC with static `10.20.10.4`
  - VM (`Standard_D2as_v5`, non-Spot, Ubuntu 24.04)
  - separate data disk `disk-researchtrack-data-prod` at LUN 0 (detached, not deleted, with the VM)
  - internal Container Apps environment (workload profiles, optional capped Log Analytics)
  - private DNS zone for the environment domain with a wildcard A record and a VNet link
- **Container Apps:** `modules/container-app.bicep` defines all 7 apps: VNet-only ingress, `/health/live` and `/health/ready` probes, 1/1 replicas, GHCR registry, `@secure()` secrets. `deploy-container-apps.sh` applies it per changed service.
- **VM bootstrap** (`scripts/configure-vm.sh`, via Run Command):
  - installs Docker, the Compose plugin, certbot and unattended-upgrades, and sets bounded Docker logs
  - mounts the data disk at `/data/researchtrack` by UUID in fstab
  - formats a disk only when it has no filesystem and no partitions
- **VM stack** (`vm/compose.yml`, `vm/scripts/reconcile-stack.sh`), all persistent state in bind mounts under `/data/researchtrack`:
  - Nginx is the only public binding.
  - MySQL 8.4 is bound to `10.20.10.4:3306` with `require_secure_transport=ON`.
  - Kafka 3.9.1 runs KRaft with a TLS listener on `10.20.10.4:9092`. The CA and broker cert are generated and renewed on the VM, with the IP SAN verified. The plaintext listener exists only on the Docker network.
  - Prometheus and Grafana bind to loopback only.
  - Grafana is public only via `https://grafana.researchtrack.blipzo.xyz`.
  - Let's Encrypt runs via host certbot (webroot), and its deploy hook reloads Nginx.
  - Daily local MySQL backups and a disk-usage warning timer.
- **Secret delivery to the VM:** non-secret config is inlined in the Run Command script. `mysql.env`, `grafana.env` and `infra.env` travel only as a Run Command protected parameter and are then written as `0600` files in `/opt/researchtrack/runtime`.
- **Workflows:**
  - New `azure-infrastructure.yml`: What-If gate that fails on deletions, deploy, VM configure/reconcile, and validations.
  - `backend-deploy-azure-production.yml`: application releases only. Preflight → migrations → Bicep app rollout → rollback → verification.
  - Both share one concurrency group.
- **Monitoring:** `vm/prometheus/prometheus.yml.template` scrapes the 7 apps over the VNet (HTTPS; the Gateway on its VNet-only port 9100). Stable labels are `service` and `environment=production`. `instance` is set to the service identity (e.g. `github:8080`), so the shared rules and dashboards are reused unchanged.
- **Gateway forwarded headers** (`src/Gateway/ResearchTrack.Gateway/TrustedProxyForwarding.cs`): `X-Forwarded-For` and `X-Forwarded-Proto` are trusted only from private or CGNAT hops; values a client prepends are ignored. This fixes per-IP rate limiting behind Nginx (Azure) and NPM (Test).
- **Validators:** `validate-azure-env-files.sh` gains an `infra` scope and a Grafana HTTPS root-URL check. The Test validator is unchanged.
- **Removed** from the earlier draft: storage account, submission identity, Blob backups/bundles, public SSH rule, the combined infra+app workflow and the SSH-tunnel Grafana.

## In progress

None.

## Known blockers

- No Azure access in the implementation environment. Every Azure-side behaviour is unverified until the workflows run.
- **Region needs confirmation:** the repository records `southeastasia` (Bicep fallback, RUNBOOK examples, and the D2as_v4 quota analysis below), but `eastasia` has been reported as the working region. Set `AZURE_LOCATION` to the real region *before* the first infrastructure run (a resource group's region cannot change afterwards), and re-check D2as_v4 quota there.
- **Jira callback/webhook paths are unknown on this branch:** `ResearchTrack.JiraService` has persistence code only. Take the paths from the Jira feature code before the Jira cutover.

Configuration and operator documentation: `docs/devops/configuration/` (added 2026-09-24).

- **Production trigger branch (2026-09-24):** the temporary `devops/azure-production-deployment` trigger was removed. `backend-deploy-production.yml` and `azure-infrastructure.yml` now run on pushes to `main`, as designed.

## First real Azure run: VM stack findings (2026-09-24)

- **Proven healthy on Azure:**
  - the data disk mount
  - Nginx, MySQL, Kafka, Prometheus and Grafana running
  - port bindings: 3306/9092 on the private IP only, 9090/3000 on loopback only
  - MySQL TLS for all six accounts, with non-TLS rejected
  - a valid Nginx config
- **Fixed** (`validate-vm-stack.sh`: Prometheus rules not loaded, Grafana datasource/dashboards missing, Kafka TLS smoke failing):
  - One root cause: `build-vm-bundle.sh` staged the non-secret config under `umask 077`, so it shipped root-only (`0600`/`0700`), and `install-bundle.sh` (`tar` + `rsync -rlt` without `-p`) preserved those modes.
  - As a result, Prometheus (uid 65534) could not read `prometheus/rules` (it silently loads 0 rules), Grafana (472) could not read `grafana/provisioning`, and the Kafka CLI (1000) could not read `kafka/client-ssl.properties`.
  - Fixes: config is normalized to world-readable in the bundle; `install-bundle.sh` uses `rsync -rlpt --chmod` so already-deployed files are repaired; `reconcile-stack.sh` fails fast if these paths are not readable; the Kafka smoke test is now offset-based, uses one exact token, and prints diagnostics.
  - Runtime secrets remain `0600`.
- **Second real run:** Prometheus rules and Grafana provisioning are now proven fixed on Azure. Kafka still failed with `AccessDeniedException: /etc/kafka/client-ssl.properties`.
  - Root cause: Kafka used **single-file bind mounts**, which pin the inode present at container creation. Each run, `rsync` saw a new checkout mtime, rewrote the file and renamed it into place (new inode, now `0644`). Kafka was not recreated because the inputs hash compares content only, so the container kept the original root-only `0600` inode.
  - Fix: Kafka now uses **directory mounts** with explicit numeric ownership:
    - `/opt/researchtrack/kafka` → `/etc/researchtrack/kafka` (non-secret, `0644`)
    - `/opt/researchtrack/runtime/kafka/server.properties` (`root:1000 0640`)
    - `/data/researchtrack/kafka/tls/container/{broker.p12 root:1000 0440, ca.crt 0644}`
  - The CA and broker private keys never enter the container. `reconcile-stack.sh` verifies readability from inside the container as uid 1000, and rejects credentials in the world-readable `client-ssl.properties`.
  - The infrastructure workflow's app-presence probe now counts only the seven exact app names and never fails. `EXPECT_APPS=true` only when all seven exist.

- **Third run:** `reconcile-stack.sh` failed with `INFRA_PRIVATE_IP 10.20.10.4 is not assigned` although Azure confirms the NIC has static `10.20.10.4`.
  - Cause: a guest-side false negative. `ip -4 -o addr show | grep -qw …` ran under `pipefail`. Once `grep -q` matched it exited, and later address records (`docker0`, `br-…`) sent `ip` a SIGPIPE (141), so the pipeline "failed". There was also no tolerance for the network settling right after Bicep and `configure-vm.sh` (apt upgrade, Docker restart).
  - Fix: `global_ipv4_addresses` captures the list first and compares exactly. It retries up to 12×5 s, then fails with diagnostics (addresses, interfaces, routes). `INFRA_PRIVATE_IP` is normalized and validated. The same capture-first fix was applied to the partition check that guards the data-disk format in `configure-vm.sh`.
  - Tests: `deploy/azure/validation/test-private-ip-check.sh` (also in CI).
  - Azure networking/Bicep unchanged.

- **Fourth run (network probe):** VM reconcile passed. The `rt-netcheck-prod` job reached MySQL, then failed with no diagnostic.
  - The warning `Additional flags were passed along with --yaml` does not come from our flags (we passed only `-g`, `-n`, `--yaml`, `-o`). `az containerapp job create` registers defaults for `--parallelism 1` and `--replica-completion-count 1`, so every `create --yaml` warns (azure-cli 2.90, `_params.py` / `set_up_create_containerapp_job_yaml`).
  - Fix: all jobs (probe and migrations) are now applied with one mechanism, an ARM PUT of the full body (`aca-job.sh`). No `--yaml` remains.
  - The probe (`aca-probe.sh`) now logs PROBE/PASS/FAIL per check: MySQL TCP, Kafka TCP, then a deterministic Kafka TLS round trip (end offset, sync produce with acks=all, assign-mode consume of one record, exact match). The seven app checks run only when `EXPECT_APPS=true`.
  - The old probe's failing step was the final `grep -qx` after a group-based `--from-beginning` consumer whose stderr was discarded.
  - Logs come from the live replica or, failing that, Log Analytics.
  - Tests: `deploy/azure/validation/test-network-probe.sh` (also in CI).

## Validation already performed (local, 2026-09-24)

- `bicep build` for `main.bicep` and `modules/container-app.bicep`, `bicep build-params` for `production.bicepparam`, and `bicep lint`: clean.
- `actionlint` on all workflows: clean. Only pre-existing SC2129 style notes remain in `backend-build-images.yml`.
- `shellcheck -S warning` and `bash -n` on every `deploy/azure` script: clean.
- `nginx -t` on the rendered edge config in HTTPS and pre-TLS modes: OK.
- `promtool check config` on the rendered Prometheus config, and `promtool check rules` on the shared rules: OK.
- `deploy/azure/validation/test-validate-azure-env-files.sh`: 11/11 pass (synthetic values generated from `.env.example`). Now also runs in CI.
- `build-vm-bundle.sh` with synthetic env files: bundle contents as expected, about 29 KB script.
- `deploy-container-apps.sh` against a stubbed `az`: first deploy, no-op, single-service deploy, unhealthy revision → deactivate + keep previous, failed migration → no Bicep deployment.
- `dotnet build -c Release`: 0 warnings. `dotnet test --filter Category!=DatabaseIntegration`: 206/206 pass, including 3 new forwarded-header tests.

## Must be proven on real Azure (first runs)

1. OIDC login and subscription-scope What-If/deploy with the `.bicepparam` file.
2. Run Command v2: `--protected-parameters` reach the script as environment variables, and the ~29 KB inline script is accepted.
3. `/dev/disk/azure/scsi1/lun0` exists for the data disk on the Ubuntu 24.04 image.
4. `apache/kafka:3.9.1` runs as uid 1000 with the custom entrypoint and `server.properties`.
5. The certificate the internal environment presents for `*.<defaultDomain>` verifies against public CAs. If not, set `ACA_TLS_VERIFY=off` (see RUNBOOK).
6. The Gateway additional TCP port 9100 is reachable from the VM.
7. Container App → MySQL (dbcheck/migrate with `SslMode=Required`) and → Kafka TLS (`network-probe.sh`).
8. Revision rollback on a real unhealthy revision.
9. The Container Apps ingress hop falls inside the trusted ranges, so the Gateway sees the real client IP (check the logs after cutover).

## Subscription-specific configuration

- **OIDC identity:** the SLIIT Entra tenant blocks `az ad app create`. GitHub Actions therefore authenticates as the user-assigned managed identity `id-researchtrack-github-prod`, which lives in `rg-researchtrack-bootstrap` (region from `AZURE_LOCATION`). Its federated credential has subject `repo:Nimsara-Jayarathna/researchtrack-backend:environment:production` and audience `api://AzureADTokenExchange`. It has Contributor at subscription scope and no client secret. `bootstrap-oidc.sh` now creates exactly this, idempotently, using `az identity create`, `az identity federated-credential create` and `az role assignment create`.

- `AZURE_VM_SIZE=Standard_D2as_v4` must be set as a GitHub `production` Environment variable. The Azure for Students subscription in `southeastasia` has 0 DASv5 quota and 4 DASv4 vCPUs. The size has only a zone-1 restriction, and no zone is pinned. The Bicep default stays `Standard_D2as_v5`.
- The infrastructure workflow exports `AZURE_VM_SIZE` only when the variable is set. Previously an unset variable became an empty `vmSize`.

## Next 3 actions

1. Human: run `bootstrap-oidc.sh` and set the GitHub `production` secrets/variables, including `AZURE_VM_SIZE=Standard_D2as_v4` (RUNBOOK §1–2).
2. Run the Azure Infrastructure workflow, then the application workflow with `force_full_build` (RUNBOOK §3). Fix whatever the real-Azure checks above expose.
3. Data migration, then DNS/TLS/callback cutover (RUNBOOK §4–5).

## Architecture deviations

1. **The Gateway metrics stay on port 9100**, exposed as a VNet-only additional TCP port instead of `/metrics` on the app port (MASTER_PLAN §34 "preferred"). Moving it would publish `/metrics` through Test's NPM, or need a new config key that breaks the Test contract. The scrape uses plain HTTP inside the VNet; the other six use HTTPS. Nginx returns 404 for `/metrics`.
2. **Container Apps are defined in Bicep** (`modules/container-app.bicep`) but applied by the application workflow, not by the infrastructure `main.bicep`. Otherwise every infrastructure deployment would reset app images and secrets. The infrastructure workflow never touches the apps.
3. **Service-to-service calls stay plain HTTP inside the Container Apps environment** (`allowInsecure` on the six backends). HTTPS→HTTP redirects would break YARP. VM → app traffic uses HTTPS.
4. **Kafka TLS covers encryption and server authentication only.** There is no client authentication (mTLS/SASL) yet; that comes with Phase 9 client integration.
5. **MySQL backups are local to the data disk** (7 kept). There is no off-VM copy because storage accounts are out of scope. Follow-up: off-VM backups.
6. **Prometheus and Grafana also bind to `127.0.0.1`** on the host for on-VM validation (allowed by MASTER_PLAN §14). The NSG denies 9090/3000 regardless.
7. **An extra short-lived Container Apps Job, `rt-netcheck-prod`** (apache/kafka image), proves Container Apps → Kafka/MySQL. It is not an application app.
8. **The Gateway forwarded-header fix changes runtime behaviour on Test too**: per-client rate limiting behind NPM. No Test deployment files or config contracts changed.
