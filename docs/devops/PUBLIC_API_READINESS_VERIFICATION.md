# Public API readiness verification — TASK 04

Verified locally on 2026-10-12 in `integration/main-kafka-production-20261011`, based on
commit `1810f09535d6a6d00947bcf84d18fde473e40c39`, with earlier TASK 02/03 changes retained.
The existing public check satisfies the requested HTTP/TLS/retry behavior. No application
or deployment workflow correction was necessary. Missing regression coverage was added;
the existing test and instructions incorrectly treated Actions string equality as case-sensitive.
No variable, secret, GitHub setting, Azure resource or runtime state was changed. No workflow
was dispatched; no commit, push or deployment was performed.

## Endpoint and acceptance contract

The `Verify public API endpoint` step in
[backend-deploy-azure-production.yml](../../.github/workflows/backend-deploy-azure-production.yml)
executes an anonymous **GET** to `https://$API_HOSTNAME/health/ready`.
`API_HOSTNAME` comes from `vars.PRODUCTION_API_HOSTNAME`, falling back to
`api.researchtrack.blipzo.xyz`. The default full URL is therefore
**https://api.researchtrack.blipzo.xyz/health/ready**; there is no `/api/v1` prefix.

| Result | Existing public gate |
|---|---|
| curl exits 0 and status is exactly 200 | Pass; stop retrying |
| 201, 204 or any other non-200 status | Fail after bounded retries |
| 301/302/303/307/308 or another redirect | Reject; no redirect following |
| 401/403/404/429 | Reject; no credentials or rate-limit bypass supplied |
| Any 5xx | Reject |
| DNS, connect, timeout, TLS handshake or certificate verification error | Reject even if curl also writes `200` |
| 200 with JSON `status: Degraded` | Public reachability passes; complete dependency health is **not proven** |

The actual command uses `-fsS --max-time 10 --output /dev/null --write-out '%{http_code}'`.
It has no `-k`/`--insecure`, `-L`/`--location`, request body, authentication header,
`--resolve`, or fallback to HTTP. Certificate chain and hostname verification use curl's
normal trusted CA configuration. This assumes the GitHub-hosted runner's curl configuration
has not been overridden to disable verification. [curl certificate verification](https://curl.se/docs/sslcerts.html)

There are at most **10 requests**, each limited to **10 seconds**, with **6-second sleeps**
after failures, including the last failure. The script's configured maximum is about
**160 seconds**, plus process scheduling overhead. Success on attempt ten has nine sleeps;
there is no curl-internal retry loop. A nonzero step exit fails deployment verification;
the existing `always()` runtime restoration still executes afterward.

## What a 200 proves

The [shared endpoint mapping](../../src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/Extensions/ApplicationBuilderExtensions.cs)
selects health checks tagged `ready`. The
[response writer](../../src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api/Health/HealthResponseWriter.cs)
preserves `status`, `checks`, `traceId`, `timestamp` and duration fields.
The unchanged ASP.NET Core mapping returns Healthy=200, Degraded=200 and Unhealthy=503.
[Microsoft health-check status mapping](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-10.0#customize-the-http-status-code)

The [Gateway](../../src/Gateway/ResearchTrack.Gateway/Program.cs) registers no downstream
readiness checks. Its normal response is `status: Healthy` with `checks: []`. The path is
anonymous, serves the Gateway's own health middleware, and does not proxy this request to
GitHub/Jira or another service. It does not write business records, invoke a webhook,
perform a migration, or publish a Kafka message. Ordinary request logs, metrics and
the general rate-limit counter can advance; an exhausted bucket can return 429.

The public workflow discards the body. Even a 200 containing another JSON status or HTML
cannot establish the response contract by itself. Treat its success as evidence of public
DNS/TLS/edge-to-Gateway reachability at that time. Use the existing separate service,
database, Kafka processing and monitoring validations for dependency health. The new
synthetic Gateway tests explicitly retain Degraded in the body despite HTTP 200.

## Configuration and observed GitHub state

Read-only repository and `production` Environment variable queries found **neither**
`PRODUCTION_VERIFY_PUBLIC_ENDPOINT` nor `PRODUCTION_API_HOSTNAME` in either scope.
The owner is a personal GitHub account, so organization variable inheritance is not applicable.
The enable flag is absent, and the hostname falls back to the workflow default.
Variables are strings: the condition is `vars.PRODUCTION_VERIFY_PUBLIC_ENDPOINT == 'true'`.
Use lowercase `true` operationally; `True` and `TRUE` also match because Actions string
comparison ignores case. Unset, `false`, `1`, `yes`, or whitespace-padded `true` do not match.
[GitHub expression comparison](https://docs.github.com/en/actions/reference/workflows-and-actions/expressions#operators)

The three latest application deployment runs returned these step conclusions:

| Run | Source SHA | Public step |
|---|---|---|
| [38153663471](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38153663471) | `1810f09535d6a6d00947bcf84d18fde473e40c39` | SKIPPED |
| [38147705025](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38147705025) | `a26cfd2b2b2093a8be45ef4a80c493af3d29958e` | SKIPPED |
| [38135611889](https://github.com/Nimsara-Jayarathna/researchtrack-backend/actions/runs/38135611889) | `6c86bdb20d4682b56babc0afcd9a6bc53793d462` | SKIPPED |

These overall-success runs do **not** supply public endpoint acceptance evidence.
Filtered read-only metadata is retained locally in
`artifacts/public-api-readiness-verification/github-readonly-evidence.json` (ignored by Git).

The infrastructure defines a static IPv4 public IP and an Nginx edge. Public DNS must point
to the intended Azure VM public IP, rather than the retired VPS or private ACA address.
Review any AAAA/CNAME/CDN path too; stale IPv6 or an intermediate authentication/redirect
can make public requests fail or hit another application.

[api-https.conf.template](../../deploy/azure/vm/nginx/api-https.conf.template) listens on
443 with `/etc/letsencrypt/live/<hostname>/fullchain.pem` and its certificate key; port 80
redirects to HTTPS except ACME. TLS 1.2/1.3 are configured. Before a certificate exists,
[api-http.conf.template](../../deploy/azure/vm/nginx/api-http.conf.template) allows only
local/private Gateway traffic and ACME challenges: that mode cannot pass this public HTTPS gate.
Certbot issuance/renewal and Nginx activation must already have completed under a separately
approved operation. The certificate must be current, publicly trusted and cover the chosen hostname.

[gateway-proxy.inc.template](../../deploy/azure/vm/nginx/gateway-proxy.inc.template) routes
to the private Gateway FQDN with SNI and the app Host header. `ACA_TLS_VERIFY` controls that
separate upstream hop and defaults to `on`; it never supplies `-k` to the public workflow.
Inspect its deployed value before claiming both TLS hops are verified. Existing
`runtime-readiness.sh`/`validate-infrastructure.sh` can test private/pre-cutover paths and
use internal TLS overrides; their success does not substitute for public DNS and certificate evidence.
No live DNS lookup, certificate inspection or public HTTP probe was performed for this task.

## Exact operator procedure — enable only after verification

1. In repository **Settings → Environments → production → Environment variables**, inspect
   `PRODUCTION_API_HOSTNAME`. If absent, use the default above. Confirm it agrees with the
   existing Nginx hostname, certificate and approved infrastructure configuration. Resolve
   any mismatch through separate change review; do not merely point the gate at another host.
2. While runtime is **already available through an approved session**, compare public DNS
   with the existing VM public IP. These commands are read-only; replace the hostname only
   if the existing approved configuration uses another value:

   ```bash
   api_hostname='api.researchtrack.blipzo.xyz'
   dig +short A "$api_hostname"
   dig +short AAAA "$api_hostname"
   dig +short CNAME "$api_hostname"
   # Optional, with an existing Azure read-only login; no VM wake is performed:
   az network public-ip show --resource-group rg-researchtrack-prod \
     --name pip-researchtrack-prod --query ipAddress --output tsv
   ```

3. From an external client with trusted CA roots, run an anonymous GET and retain the status,
   headers and body locally. Do not use `-k`, `-L`, `--resolve`, a cookie, bearer token or
   performance bypass; do not issue POST/PUT/DELETE or replay events. `-q` as the first option
   suppresses a client `.curlrc` override for this manual check:

   ```bash
   set -euo pipefail
   api_hostname='api.researchtrack.blipzo.xyz'
   public_probe_dir="$(mktemp -d "${TMPDIR:-/tmp}/researchtrack-public-readiness.XXXXXX")"
   public_status="$(curl -q -fsS --max-time 10 \
     --dump-header "$public_probe_dir/headers.txt" \
     --output "$public_probe_dir/health.json" --write-out '%{http_code}' \
     "https://$api_hostname/health/ready")"
   [[ "$public_status" == 200 ]] || { echo "Rejected HTTP $public_status" >&2; exit 1; }
   cat "$public_probe_dir/headers.txt"
   python3 - "$public_probe_dir/health.json" <<'PY'
   import json, sys
   with open(sys.argv[1]) as handle:
       health = json.load(handle)
   assert health['status'] in ('Healthy', 'Degraded'), health
   assert isinstance(health['checks'], list), health
   assert all(key in health for key in ('traceId', 'timestamp', 'totalDurationMs')), health
   print(json.dumps(health, indent=2))
   print('Public reachability verified; complete dependency health remains a separate check.')
   PY
   ```

   Expected curl exit 0, HTTP 200, `Content-Type: application/json`, and the Gateway health
   fields described above. Inspect certificate hostname, chain and expiry in a trusted
   client's certificate viewer; retain the dates/issuer/SAN and probe timestamp. The GET
   must reach this Gateway, not a login page or old VPS. Degraded needs separate diagnosis
   and must never be recorded as proof that every dependency is healthy.
4. Only after recording DNS, trusted certificate and anonymous response-contract evidence,
   go to **Settings → Environments → production → Environment variables → Add variable**
   (or edit the existing variable): name **PRODUCTION_VERIFY_PUBLIC_ENDPOINT**, value
   **true**, then save. It is an Environment **variable**, not a secret; do not change
   identity, federation, credentials, branch protections, triggers or runtime settings.
5. At the next separately approved application release, confirm `Verify public API endpoint`
   is **success**, rather than **skipped**, and retain its run URL/SHA plus runtime-restoration
   result. Enabling the variable alone neither runs nor deploys anything. A runtime originally
   asleep will be restored asleep afterward; later public downtime is not evidence that
   the in-deployment probe failed. No release should be dispatched solely by this task.

If preconditions fail, leave the flag unset/false and diagnose DNS, certificate issuance or
expiry, ingress routing, 429 limits, and private Gateway availability. If an approved enable
must be reversed, set the same variable to `false` through the same UI with operator approval;
this affects future checks, does not repair an outage, and does not roll back images or data.

## Executed validation and local changes

| Validation | Actual result |
|---|---|
| Full offline workflow release-gate suite | **PASS — 31 tests**, including 8 public-readiness tests |
| Release-policy regression suite | **PASS — 22 tests** |
| Gateway .NET Release test project/build | **PASS — 28 tests, 0 failed, 0 skipped** |
| Existing runtime-readiness suite | **PASS — 4 mocked scenarios**; no VM commands |
| Raw actionlint 1.7.12 on the public-check workflow | **FAIL — existing SC2129 style advisory** at configuration step line 101, unrelated to readiness |
| actionlint with only that SC2129 advisory excluded | **PASS**; no workflow behavior changed |
| Public shell syntax, workflow YAML parsing, documentation links and `git diff --check` | **PASS** |
| Live public DNS/TLS/HTTP, future enabled Actions run | **NOT EXECUTED** |

The workflow suite executes the actual public step shell with synthetic curl responses and
stub sleeps; it verifies the exact secure GET arguments, disabled/no-request behavior,
Actions case-insensitive flag comparison, non-200 rejection (including required 4xx and
representative 5xx), transport/TLS failures, bounded retries/sleeps, recovery, and tenth-attempt
success. It uses no network or production credentials. Four added Gateway test cases use the
existing in-process factory to verify anonymous health JSON, the empty dependency set,
ready-tag selection, and Healthy/Degraded/Unhealthy HTTP mappings with synthetic checks.

TASK 04 changes only:

- [test_workflow_release_gates.py](../../deploy/azure/validation/test_workflow_release_gates.py): extend the existing public readiness fixture and missing cases; correct flag simulation.
- [InfrastructureSmokeTests.cs](../../tests/ResearchTrack.Gateway.Tests/Integration/InfrastructureSmokeTests.cs): add real Gateway-pipeline readiness tests.
- [GITHUB_ENVIRONMENTS.md](configuration/GITHUB_ENVIRONMENTS.md): correct flag case semantics and link operator instructions.
- This report. Earlier TASK 02/03 local files remain for review.

Reproduce without Docker or production access:

```bash
python3 -m unittest deploy/azure/validation/test_workflow_release_gates.py -v
python3 deploy/azure/validation/test_release_policy.py
bash deploy/azure/validation/test-runtime-readiness.sh
dotnet test tests/ResearchTrack.Gateway.Tests/ResearchTrack.Gateway.Tests.csproj \
  --configuration Release --no-restore
```

Use Python 3, Bash, .NET 10 and existing restored NuGet dependencies (`dotnet restore` if
needed). This task used Python 3.14.7 and .NET SDK 10.0.400 on macOS. Gateway TRX evidence is
`artifacts/public-api-readiness-verification/dotnet/gateway-readiness.trx`; offline logs and
result metadata are in the same ignored artifact directory. Full solution/Kafka suites were
not rerun for this test/documentation-only task; production dependency and certificate
evidence remains an operator prerequisite.
