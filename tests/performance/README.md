# SE3112 — ResearchTrack k6 performance testing (Nimsara)

**Target:** the already deployed Azure ResearchTrack Gateway and microservices, driven by the **same VPS and SSH account as the existing Test deployment**. No second SSH account, private key, hostname or user is needed. This remains a manually triggered Azure runtime session, not a pull-request performance claim against undeployed code.

## Architecture

```text
workflow_dispatch (smoke/load/stress/spike/write/lifecycle/webhook)
  ├─ existing GitHub secrets: SSH_HOST / SSH_PORT / SSH_USER / SSH_PRIVATE_KEY
  ├─ Azure OIDC + existing runtime-power snapshot
  ├─ (when enabled) discover CURRENT VPS HTTPS egress IPv4 via two observers
  ├─ (when enabled) snapshot Gateway single-revision template and traffic
  └─ existing runtime-session.sh: start VM, services and readiness checks
       └─ run-gateway-session.sh
            ├─ (when enabled) Azure app secret + temporary Gateway revision
            ├─ run-vps.sh → existing VPS SSH → k6 → reports
            └─ restore original Gateway template + remove transient secret
       └─ existing runtime-session.sh restores original VM/app power state
  └─ independent always() Gateway/power restore safeguards + report artifacts
```

The Gateway's **general** 120/min/IP rate-limit bucket can be exempted temporarily for requests satisfying both the actual forwarded VPS IP and the secret `X-ResearchTrack-Performance-Key` header. The normal login, refresh, password reset and dedicated GitHub-webhook limits are **not** exempted; authentication and role permissions still apply. The Gateway strips the special header before proxy forwarding. No source-IP rotation or source-code hardcoding is used.

## Configuration you need to create

### Existing repository-level Actions secrets — **reuse these exact names**

| Existing secret | Purpose |
|---|---|
| `SSH_HOST` | Existing Test VPS host for SSH |
| `SSH_PORT` | Existing VPS SSH port |
| `SSH_USER` | Existing VPS Unix username |
| `SSH_PRIVATE_KEY` | Existing VPS SSH private key |

These are the same repository secrets used in `.github/workflows/backend-deploy-reusable.yml`. No `K6_VPS_HOST`, `K6_VPS_USER`, `K6_VPS_SSH_KEY`, `K6_VPS_KNOWN_HOSTS`, static VPS-IP variable or new VPS user is required. Remote k6 scripts and credentials are transferred to a temporary `0700` directory and removed at the end, including failed threshold runs.

The SSH host-key discovery mirrors existing Test deployment (`ssh-keyscan` + `StrictHostKeyChecking=yes`). **It is not an independently verified fingerprint pin**; a first-connection MITM risk remains. Future hardening should pin an out-of-band verified VPS host fingerprint in both deployment and performance workflows.

### **One new repository-level variable** (Settings → Secrets and variables → Actions → Variables)

| Variable | Value | Effect |
|---|---|---|
| **`K6_RATE_LIMIT_BYPASS_ENABLED`** | `true` / `false` | `true` activates a temporary Gateway revision scoped to the current VPS egress IP and k6 token. `false` (or missing) leaves the deployed Gateway unchanged and keeps ordinary limits. |

There is **no manually set Gateway IP in GitHub**. If bypass is true, `discover-vps-ip.sh` SSHes to the VPS and checks its public IPv4 through `https://api.ipify.org` and `https://ipv4.icanhazip.com`. Both observers must agree on a globally routable IPv4 before the Gateway may be altered. If SSH/NAT routing does not make the Gateway see this as the actual client address, normal 429 limits remain and the test fails visibly instead of falsely claiming capacity.

### GitHub **production environment** variables

| Variable | Value |
|---|---|
| `K6_BASE_URL` | Actual deployed HTTPS Gateway origin, e.g. `https://api.researchtrack.blipzo.xyz` (verify) |
| `K6_PROJECT_ID` | Dedicated testing research-project UUID |
| `K6_STUDENT_ID` | Student UUID **only needed for write/lifecycle** |
| `K6_INCLUDE_INTEGRATIONS` | `false` by default; `true` for linked dedicated GitHub/Jira read endpoints |
| `K6_P95_MS` | Optional evidence-calibrated p95 latency threshold |
| `K6_JIRA_PROJECT_KEY` / `K6_JIRA_ISSUE_KEY` / `K6_JIRA_ISSUE_ID` | Optional dedicated Jira fixtures for `webhook` |

The Azure workflow reuses existing environment/repository Azure variables and OIDC secrets (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`).

### GitHub **production environment** secrets

| Secret | Purpose |
|---|---|
| `K6_SUPERVISOR_EMAIL` / `K6_SUPERVISOR_PASSWORD` | Dedicated ResearchTrack supervisor session |
| `K6_STUDENT_EMAIL` / `K6_STUDENT_PASSWORD` | Dedicated ResearchTrack student session |
| **`K6_PERFORMANCE_TOKEN`** | 32+ character random secret, required **only when** `K6_RATE_LIMIT_BYPASS_ENABLED=true`. GitHub temporarily provisions it as an Azure Container App **secret reference**, never a plaintext revision environment value. |
| `K6_JIRA_WEBHOOK_CLIENT_SECRET` | Only if executing the explicit Jira webhook test |

Do not provide passwords, Azure credentials or SSH keys in chat, PRs, output artifacts or `.env` source files. Store them in protected GitHub secrets. Account sessions use `ss_access_token` HttpOnly cookies; two logins occur during k6 `setup()`, not per virtual user iteration.

## Gateway deployment prerequisite

**Deploy the Gateway code including `PerformanceRateLimitPolicy` once** before using the new workflow. Keep the permanent production `gateway.env` contract in its ordinary **disabled** state:

```dotenv
RateLimiting__PerformanceTest__Enabled=false
RateLimiting__PerformanceTest__AllowedIp=0.0.0.0
RateLimiting__PerformanceTest__Token=DISABLED
```

Do **not** manually replace these with a fixed IP or toggle them on in your production config. When the repository variable is true, the workflow handles this automatically at runtime:

1. Capture the current Gateway `Single` revision name, **entire revision template** (image, env, resources, etc.) and ingress traffic rules, while the original exemption is disabled.
2. Obtain/validate the actual VPS egress IPv4.
3. Add a uniquely named, run-scoped Azure Container App secret containing `K6_PERFORMANCE_TOKEN`.
4. Create a temporary revision **copied from the original** with the general rate-limit exemption enabled, allowed IP set to the discovered IP, and the token referenced with `secretref:`.
5. Wait until the new revision reports ready and has exactly the intended exemption environment values. Run k6.
6. Create a new revision by **copying the original revision back**, restoring the complete previous template and latest=100 routing behavior; remove the now-unused test secret and verify.
7. Restore and verify the original VM + Container Apps power states using existing runtime scripts.

A rollback in Azure Single mode produces a **new revision with the same original template**, not the same original revision identifier. The recovery snapshot records the original revision so it can always be copied back. For safety this automation **rejects Multiple-revision mode, non-latest traffic routing, a pending revision or an exemption already enabled** instead of guessing which revision should receive traffic. Because each run creates temporary revisions, older revision history may remain until normal Azure pruning.

Secrets are application-scoped in Azure Container Apps, so the original revision must be restored before removing the transient secret. If Azure rejects deletion, the workflow flags a cleanup failure and preserves its independent recovery artifact; it does not declare green CI just because k6 passed.

## VPS prerequisites

Use the existing Test VPS and SSH account (`SSH_USER`). Install the k6 CLI and verify that this existing account can run these commands:

```bash
k6 version
command -v flock
command -v timeout
command -v curl
```

The VPS must be able to reach the Azure production Gateway by HTTPS, the two IP discovery HTTPS sites (when bypass is enabled), and the same SSH port already used by Test CI must be accessible from GitHub-hosted runners. You can test port/auth separately using the existing Test deployment workflow. No additional Azure runner, VPS user or server-side long-lived k6 credential file is necessary.

## k6 profiles

| Profile | Example workload | Notes |
|---|---|---|
| `smoke` | 2 virtual users, 20 seconds | Real authenticated project/submission/dashboard reads |
| `load` | 2→10 iterations/s mixed reads, 135 seconds | Requires exemption for meaningful capacity metrics beyond 120/min/IP |
| `stress` | 5→40 iterations/s mixed reads, 135 seconds | Ramp demand, observe saturation |
| `spike` | 2→40→2 iterations/s, 55 seconds | Sudden read-traffic burst/recovery |
| `write` | ~10 submission-requirement create/delete iterations | Only in dedicated testing project; retains data if interrupted |
| `lifecycle` | Student v1 upload → supervisor feedback → student v2 upload → approve | Real file/object-storage integration; durable version history |
| `webhook` | Up to 12 signed Jira webhooks/s | Requires dedicated linked Jira fixture; may trigger provider sync and rate limits |

The original k6 scripts, validation of HTTP responses, percentile thresholds, summary/report generation, and existing endpoint choices have been preserved. In the `false` bypass mode, high-rate load/stress/spike profiles **can legitimately return HTTP 429 and fail**; use `true` when measuring backend capacity. If the bypass is enabled but 429s still occur, check actual Azure-observed forwarded client IP and any additional downstream limiter.

## Run and verify

1. Configure the repository variable and environment secrets/variables above.
2. Confirm the updated Gateway **image has been deployed** (source-only updates do not change the running Azure image).
3. GitHub → **Actions** → **Azure Production - k6 Performance (VPS)** → **Run workflow**.
4. Choose `smoke` first, type the existing explicit confirmation `RUN_K6_PERFORMANCE`, and approve the `production` environment if protected.
5. Check Gateway snapshot, dynamic VPS IP step, readiness, k6 log/threshold results, Gateway restoration verification and VM/container-app restoration verification.
6. Download `k6-<profile>-<run>` report artifact (JSON summary, Markdown report, raw k6 log), `azure-k6-runtime-state-<run>` power-state snapshot and (if enabled) `azure-k6-gateway-state-<run>` revision recovery snapshot.

A failed k6 threshold returns nonzero and fails the workflow **even when both restoration steps succeed**. A failed restoration also fails the workflow. `if: always()` recovery steps provide a second attempt in case the in-session script cannot restore the environment. They cannot guarantee recovery from simultaneous runner and Azure control-plane outages.

The profile measures the **currently deployed version**, not code on a not-yet-deployed branch. A green PR CI job only validates script contracts and mocked automation, not real Azure throughput.

## Code and offline CI checks

```bash
./tests/performance/validate.sh
./tests/performance/test-run-vps.sh
./tests/performance/test-discover-vps-ip.sh
./tests/performance/test-gateway-exemption.sh
# Existing Azure power/readiness validations also remain in backend-ci.yml
```

These tests deliberately use **fake Azure CLI and fake SSH/k6 clients**; they do not consume VPS or Azure infrastructure. Real `.NET` Gateway unit tests continue through `./scripts/test.sh all` in CI. No actual performance metrics can be certified until a real Azure/VPS run succeeds.

**Persistent data:** Runtime state restoration is VM/app power only. Test project/account isolation is useful, but does not roll back MySQL records, Jira synchronization events or S3 objects. `write`, `lifecycle`, and `webhook` use a dedicated project and produce data intentionally; cleanup behavior is documented in their test scripts.
