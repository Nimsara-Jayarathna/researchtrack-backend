# SE3112 — ResearchTrack k6 performance testing (Nimsara)

**Target:** the deployed Azure ResearchTrack Gateway and microservices, driven from the **same VPS and SSH account as the existing Test deployment**. Heavy profiles are manual. A lightweight authenticated Smoke test can run automatically after a successful Production deployment.

## Final architecture

```text
Manual performance path
workflow_dispatch (smoke/load/stress/spike/write/lifecycle/webhook)
  └─ azure-performance-k6-reusable.yml
       ├─ Azure runtime snapshot/start/readiness
       ├─ optional dynamic VPS-IP + temporary Gateway exemption
       ├─ existing VPS SSH → k6 → detailed reports
       ├─ restore Gateway when changed
       └─ restore exact Azure runtime power state

Production CI/CD path
Backend Deploy - Production
  ├─ build/deploy/verify
  └─ if K6_AUTO_SMOKE_ENABLED=true
       └─ automatic Smoke via the same reusable workflow
            ├─ normal Gateway rate limiting (no bypass)
            ├─ authenticated reads + integrations when enabled
            ├─ runtime-configured thresholds
            └─ PASS/FAIL becomes part of the deployment workflow result
```

The Gateway's **general** 120/min/IP rate-limit bucket can be exempted temporarily for manual capacity testing when requests satisfy both the actual forwarded VPS IP and the secret `X-ResearchTrack-Performance-Key` header. Normal login, refresh, password-reset and dedicated webhook limits are not exempted. Automatic post-deployment Smoke deliberately runs with the exemption **off** so it exercises normal Gateway behaviour.

## Existing repository-level Actions secrets — reuse these exact names

| Existing secret | Purpose |
|---|---|
| `SSH_HOST` | Existing Test VPS host for SSH |
| `SSH_PORT` | Existing VPS SSH port |
| `SSH_USER` | Existing VPS Unix username |
| `SSH_PRIVATE_KEY` | Existing VPS SSH private key |

No `K6_VPS_HOST`, `K6_VPS_USER`, `K6_VPS_SSH_KEY`, `K6_VPS_KNOWN_HOSTS`, static VPS-IP variable or new VPS user is required. k6 source and short-lived credentials are transferred to a temporary `0700` directory and removed after pass or failure.

## Repository/production Actions variables

`K6_AUTO_SMOKE_ENABLED` and `K6_RATE_LIMIT_BYPASS_ENABLED` should be **repository-level Actions variables**, because their values are evaluated by the caller workflows before the reusable job enters the `production` environment. The remaining k6 variables may also be repository-level (simplest), or production-environment variables where appropriate.

The workflow reads thresholds at **runtime**. Final values therefore live in GitHub Settings rather than source code, so changing an evidence-based threshold does **not** require another code push.

### Execution variables

| Variable | Typical value | Purpose |
|---|---|---|
| `K6_RATE_LIMIT_BYPASS_ENABLED` | `true` for manual load/stress/spike | Enables the temporary IP+token Gateway exemption for manual runs only |
| `K6_AUTO_SMOKE_ENABLED` | `false` while baselining, then `true` | Enables automatic Smoke after a successful Production deployment |
| `K6_BASE_URL` | `https://api.researchtrack.blipzo.xyz` | Deployed HTTPS Gateway origin |
| `K6_PROJECT_ID` | dedicated project UUID | Performance-test project |
| `K6_STUDENT_ID` | dedicated student UUID | Required by write/lifecycle |
| `K6_INCLUDE_INTEGRATIONS` | `true` when Jira/GitHub are linked | Includes Jira Issues, Jira Sprint Progress and GitHub Activity in read workloads |
| `K6_WARMUP_PASSES` | `1` | Endpoint warm-up passes before measured traffic; valid 0–5 |
| `K6_JIRA_PROJECT_KEY` / `K6_JIRA_ISSUE_KEY` / `K6_JIRA_ISSUE_ID` | dedicated Jira fixture | Required only for webhook profile |

### Profile-specific overall p95 variables

Set these **after** you collect baseline evidence for that profile:

| Variable | Used by |
|---|---|
| `K6_SMOKE_P95_MS` | Smoke |
| `K6_LOAD_P95_MS` | Load |
| `K6_STRESS_P95_MS` | Stress |
| `K6_SPIKE_P95_MS` | Spike |
| `K6_WRITE_P95_MS` | Write |
| `K6_LIFECYCLE_P95_MS` | Submission lifecycle |
| `K6_WEBHOOK_P95_MS` | Jira webhook |

If a profile variable is missing, the source has a documented fallback so baseline runs still execute and still produce reports. A threshold failure during baselining is valid evidence, not an infrastructure failure.

The manual workflow also provides an optional **one-run** `p95_override_ms` input. It takes precedence over the profile variable only for that run. Do not use it as the normal long-term configuration.

### Optional endpoint-specific p95 gates

Use one GitHub variable:

```text
K6_ENDPOINT_P95_THRESHOLDS_JSON
```

Its value is a JSON object keyed by the stable operation names shown in the detailed report. Example **after** evidence review:

```json
{"jira_issues":3000,"jira_sprint_progress":1000,"github_activity":1000}
```

This is especially useful for **Jira Issues**, which the detailed reports already showed can have a very different tail-latency distribution from the ~fast core routes. It lets Jira Issues have a justified target without weakening every endpoint to the same number.

Supported operation keys include `projects`, `project_details`, `requirements`, `submissions`, `jira_issues`, `jira_sprint_progress`, `github_activity`, `supervisor_dashboard`, write/lifecycle operations, and `jira_webhook`. Unknown keys or non-positive thresholds fail early.

Correctness gates are intentionally **not calibrated away**: failure-rate limits, checks >99%, and HTTP 429 = 0 remain code-defined quality rules.

## Production environment secrets

| Secret | Purpose |
|---|---|
| `K6_SUPERVISOR_EMAIL` / `K6_SUPERVISOR_PASSWORD` | Dedicated ResearchTrack supervisor session |
| `K6_STUDENT_EMAIL` / `K6_STUDENT_PASSWORD` | Dedicated ResearchTrack student session |
| `K6_PERFORMANCE_TOKEN` | 32+ character secret used only when the manual Gateway exemption is enabled |
| `K6_JIRA_WEBHOOK_CLIENT_SECRET` | Required only for explicit Jira webhook testing |

Do not put passwords, Azure credentials or SSH keys in source, reports, PRs or chat logs. Account sessions use ResearchTrack's HttpOnly cookie authentication and are established during k6 `setup()` rather than per VU iteration.

## Gateway deployment prerequisite

Deploy the Gateway code containing `PerformanceRateLimitPolicy` once. Keep permanent Production configuration disabled:

```dotenv
RateLimiting__PerformanceTest__Enabled=false
RateLimiting__PerformanceTest__AllowedIp=0.0.0.0
RateLimiting__PerformanceTest__Token=DISABLED
```

When manual bypass is enabled, the workflow dynamically discovers the VPS egress IPv4, captures the complete current Gateway revision/routing state, creates a temporary run-scoped revision/secret, runs k6, recreates the original template and removes the temporary secret. Independent `always()` safeguards also verify Gateway and Azure-runtime restoration.

## VPS prerequisites

Use the existing Test VPS/SSH account. Install and verify:

```bash
k6 version
command -v flock
command -v timeout
command -v curl
```

No additional Azure runner, VPS user or long-lived server-side k6 credential file is required.

## k6 profiles

| Profile | Workload | Intended use |
|---|---|---|
| `smoke` | **2 VUs for 45 seconds** | Fast deployment health/correctness and low-load latency; also the automatic post-deploy gate |
| `load` | 2→10 iterations/s mixed reads, 135 seconds | Expected concurrent workload / steady-state capacity |
| `stress` | 5→40 iterations/s mixed reads, 135 seconds | Saturation and latency degradation |
| `spike` | 2→40→2 iterations/s, 55 seconds | Sudden burst and recovery |
| `write` | bounded submission-requirement create/delete | Dedicated project only |
| `lifecycle` | student v1 → supervisor changes → student v2 → approval | Full submission/version-history path including object storage |
| `webhook` | up to 12 signed Jira webhooks/s | Dedicated linked Jira fixture |

Read-oriented profiles warm selected endpoints before measured traffic. Warm-up traffic is excluded from custom business metrics but a broken warm-up route fails early.

## Baseline first, then configure thresholds — no calibration profile

There is intentionally **no separate `calibration` profile**. Use the real profiles you will present in the assessment.

Recommended process:

1. Keep `K6_AUTO_SMOKE_ENABLED=false` while establishing thresholds.
2. Run each real profile several times with unchanged workload/configuration. Start with Smoke, then Load, Stress, Spike, Write, Lifecycle and Webhook as applicable.
3. Download the HTML/JSON/CSV reports even when the current fallback threshold makes the run red.
4. Review stable p50/p90/p95/p99, throughput, failures and endpoint distributions across runs.
5. Pay particular attention to **Jira Issues**. The report includes a dedicated Jira Issues diagnostic and request count; do not finalize its p95 from only a handful of samples.
6. Choose justified overall thresholds with documented operational headroom and add them to `K6_<PROFILE>_P95_MS` GitHub variables.
7. If a specific endpoint needs its own justified limit, add it to `K6_ENDPOINT_P95_THRESHOLDS_JSON` (for example `jira_issues`).
8. Rerun the same profiles. Reports now show the configured threshold, its runtime source, and PASS/FAIL.
9. Freeze the values for the assessment. Do not keep raising them merely to make CI green.
10. After Smoke is finalized, set `K6_AUTO_SMOKE_ENABLED=true`.

This separates **test implementation** from **environment quality criteria**: source defines the workload and metrics, baseline evidence defines appropriate values, GitHub variables store the approved criteria, and CI enforces them.

## Automatic Smoke after Production deployment

`Backend Deploy - Production` now has a second job:

```text
Build and Deploy Azure Production
        ↓ success
Automatic post-deployment k6 Smoke
```

The Smoke job runs only when `K6_AUTO_SMOKE_ENABLED=true`. It reuses the same performance workflow implementation and production secrets, but forces `rate_limit_bypass_enabled=false`. If Smoke crosses a configured threshold, k6 exits nonzero and the **same Production deployment workflow is red** while reports and restoration evidence are still uploaded.

Heavy profiles remain manual; Stress/Spike/Write/Lifecycle/Webhook are not automatically executed after every deployment.

## Manual run

1. GitHub → **Actions** → **Azure Production - k6 Performance (VPS)** → **Run workflow**.
2. Choose a profile.
3. Normally leave `p95_override_ms` blank so the run uses its GitHub profile variable (or fallback during baselining).
4. Enter `RUN_K6_PERFORMANCE`.
5. Approve the `production` environment if protected.
6. Review the job summary and download the k6 artifact.

For manual Load/Stress/Spike capacity measurement, set `K6_RATE_LIMIT_BYPASS_ENABLED=true`; otherwise normal 120/min/IP Gateway limiting can produce legitimate 429 failures. Automatic Smoke ignores this switch and always uses ordinary rate limiting.

## Detailed diagnostic reports (v3)

Every real run writes four report files plus the raw k6 log, including failed thresholds:

| File | Purpose |
|---|---|
| `<profile>-report.html` | Self-contained presentation-ready report |
| `<profile>-report.md` | GitHub Actions job summary |
| `<profile>-summary.json` | Structured v3 results, threshold configuration and raw metrics |
| `<profile>-endpoints.csv` | Spreadsheet-friendly endpoint data |
| `<profile>.log` | Raw k6 console output |

Reports include request counts, throughput, failure rate, p50/p90/p95/p99/max, 2xx/3xx/4xx/5xx/429/network errors, slowest endpoints, configured threshold sources, endpoint-specific gates, and a dedicated Jira Issues diagnostic. The endpoint CSV includes each operation's configured p95 target/source and gate status.

A threshold failure remains a valid performance result: report generation happens before k6 returns its non-zero threshold exit code, then Gateway/Azure restoration still runs.

## Code and offline CI checks

```bash
./tests/performance/validate.sh
./tests/performance/test-report.sh
./tests/performance/test-run-vps.sh
./tests/performance/test-discover-vps-ip.sh
./tests/performance/test-gateway-exemption.sh
```

Backend CI checks JavaScript/shell syntax, runtime threshold selection/reporting, reusable workflow wiring, automatic post-deploy Smoke contract, mocked VPS report retrieval/cleanup, dynamic IP discovery and Gateway/Azure restoration logic without consuming real infrastructure.

**Persistent data:** Azure runtime restoration restores power/runtime state, not MySQL/S3/Jira data. `write`, `lifecycle` and `webhook` intentionally use dedicated test data and may leave evidence according to their scripts.
