# ResearchTrack JMeter performance tests

This suite measures realistic concurrent, read-heavy activity through the ResearchTrack API Gateway. It exercises ResearchTrack's stored project and Jira snapshots; it does not call Jira Cloud, start OAuth, connect/disconnect Jira, create webhooks, or invoke the manual Jira synchronization endpoint.

## Architecture and covered routes

The React application calls the YARP gateway. The performance plan follows the same public routing model and repeatedly performs this user journey:

1. `GET /api/v1/projects/{projectId}`
2. wait a randomized interval
3. `GET /api/v1/projects/{projectId}/jira/issues`
4. wait
5. `GET /api/v1/projects/{projectId}/jira/sprint-progress`
6. wait
7. `GET /api/v1/projects/{projectId}/jira/workload`
8. wait
9. `GET /api/v1/projects/{projectId}/jira/sync-state`
10. wait and repeat while the scenario is active

These routes resolve to Project Service and Jira Service through the gateway. The Jira endpoints read ResearchTrack's synchronized database snapshot. `/jira/refresh` is deliberately excluded because it contacts Jira Cloud.

## Discovered authentication contract

ResearchTrack authentication is cookie-based:

- Login is `POST /api/v1/auth/login` with JSON `{ "email": "...", "password": "..." }`.
- A successful login response body contains the user profile, not tokens.
- The Auth Service sets `ss_access_token` and `ss_refresh_token` as HttpOnly, SameSite=Strict cookies.
- The access cookie is scoped to `/api`; the refresh cookie is scoped to `/api/v1/auth`.
- `POST /api/v1/auth/refresh` has no token body. It reads the refresh cookie, rotates the database-backed one-time refresh token, and sets replacement access/refresh cookies.
- `POST /api/v1/auth/logout` revokes the refresh token and clears both cookies.
- Protected services accept the access JWT from either the access cookie or a standard Bearer header.
- Access-token lifetime is environment-owned through `Jwt__AccessTokenMinutes`; refresh lifetime uses `Jwt__RefreshTokenDays`.

The test performs exactly one setup login through the real endpoint using a JMeter HTTP Cookie Manager. It extracts only the returned access JWT and shares that Bearer token with the read-only virtual users. It does **not** share the one-time refresh token.

This avoids an authentication stampede and avoids measuring the gateway's login limiter instead of the read APIs. Sharing a rotating refresh token across 10–100 concurrent threads would be incorrect: the first refresh rotates it and the rest become replays. Before workload threads start, the setup validates the JWT `exp` claim and refuses to run unless the token covers the entire scenario plus a configurable safety margin. There is therefore no `401 → refresh → retry` loop. Any workload `401` is preserved as an authentication failure in the evidence.

A pre-issued access JWT can be supplied instead of credentials. It receives the same expiry validation. Never supply a Jira OAuth token or refresh token.

## Gateway rate limiting

The gateway uses per-client-IP fixed one-minute windows with no queue:

- general API traffic: 120 requests/minute
- login: 10 requests/minute
- refresh: 30 requests/minute
- forgot password: 5 requests/minute
- reset password: 10 requests/minute
- GitHub webhooks: 600 requests/minute

The public Test deployment will normally see all JMeter users from one client IP. A `429` therefore means the configured gateway protection activated; it is not equivalent to a backend `5xx`, and the suite never weakens the limiter to hide it.

## Configuration

For local execution:

```bash
cp tests/performance/config/.env.example tests/performance/config/.env.local
chmod 600 tests/performance/config/.env.local
```

For the deployed Test environment:

```bash
cp tests/performance/config/test.env.example tests/performance/config/.env.local
chmod 600 tests/performance/config/.env.local
```

Required non-secret values:

- `PERF_BASE_URL`: gateway origin only, with no path/query; for example `http://localhost:5000` or `https://test.api.researchtrack.blipzo.xyz`.
- `PERF_PROJECT_ID`: an existing project GUID readable by the test account.

Recommended authentication values:

- `PERF_AUTH_EMAIL`
- `PERF_AUTH_PASSWORD`

Alternative authentication value:

- `PERF_JWT_TOKEN`: raw ResearchTrack access JWT without the word `Bearer`. Leave both credential values empty when using it.

Optional controls:

- `PERF_CONNECT_TIMEOUT_MS` (default `10000`)
- `PERF_RESPONSE_TIMEOUT_MS` (default `30000`)
- `PERF_THINK_TIME_MIN_MS` (default `2000`)
- `PERF_THINK_TIME_MAX_MS` (default `5000`)
- `PERF_STRESS_THINK_TIME_MIN_MS` (default `250`)
- `PERF_STRESS_THINK_TIME_MAX_MS` (default `1000`)
- `PERF_TOKEN_EXPIRY_MARGIN_SECONDS` (default `60`)

The normal scenarios use a Uniform Random Timer with the configured 2–5 second defaults. Threads ramp up gradually and are not synchronized. PERF-005 uses its separate, more aggressive pacing controls.

`.env.local`, `*.jtl`, JMeter logs/reports, and `tests/performance/results/` are ignored by Git. The runner writes secrets only to a mode-`600` temporary JMeter properties file and removes it on exit. It does not print passwords or tokens.

## Install and run on macOS

```bash
brew install jmeter
jmeter --version
```

From the backend repository root:

```bash
./tests/performance/scripts/run-performance-tests.sh baseline
./tests/performance/scripts/run-performance-tests.sh normal
./tests/performance/scripts/run-performance-tests.sh moderate
./tests/performance/scripts/run-performance-tests.sh higher
./tests/performance/scripts/run-performance-tests.sh stress
```

Use a different uncommitted configuration file when needed:

```bash
PERF_ENV_FILE=/secure/path/test.env \
  ./tests/performance/scripts/run-performance-tests.sh normal
```

## Scenarios

| Scenario | Users | Ramp-up | Duration | Purpose |
| --- | ---: | ---: | ---: | --- |
| PERF-001 Baseline | 1 | 1 s | 2 min | Baseline latency with realistic pacing |
| PERF-002 Normal Usage | 10 | 30 s | 5 min | Typical concurrent navigation |
| PERF-003 Moderate Load | 25 | 60 s | 5 min | Moderate concurrency |
| PERF-004 Higher Load | 50 | 120 s | 10 min | Sustained higher concurrency |
| PERF-005 Stress / Protection | 10 → 25 → 50 → 100 | staged | 2 + 2 + 3 + 3 min | Degradation, protection activation, and stability |

PERF-001 through PERF-004 are not throughput attacks. PERF-005 deliberately uses shorter think time and separate stage reports so the protection/degradation point can be compared without confusing `429` with `5xx`.

## Assertions and status interpretation

Every read sampler requires HTTP `200` and a JSON ResearchTrack envelope containing `success=true` and `data`. A fast error response cannot masquerade as good latency. Setup authentication must return `200`, produce `ss_access_token`, and provide enough remaining JWT lifetime.

JMeter still marks non-200 samples as errors, while `status-summary.csv` and `status-summary.md` explain why:

| Status | Interpretation |
| --- | --- |
| 2xx | Successfully served request |
| 401 | Authentication/test-infrastructure failure |
| 403 | Authenticated account lacks authorization |
| 429 | Gateway rate limiter activated; controlled rejection and an unsuccessful business request |
| 5xx | Gateway/service/backend failure |
| Other | Unexpected and requiring investigation |

Large `429` counts in PERF-005 are useful protection evidence. Large `429` counts in PERF-002 indicate that the configured per-IP protection threshold was exceeded by the modeled workload and should be discussed alongside pacing and deployment topology. Do not relabel `429` as success.

## Evidence and reports

Each run creates an isolated directory:

```text
tests/performance/results/PERF-002-YYYYMMDD-HHMMSS/
├── jmeter.log
├── results.jtl
├── run-info.txt
├── status-summary.csv
├── status-summary.md
└── report/
    └── index.html
```

PERF-005 creates one such evidence set per stage. Open a macOS report with:

```bash
open tests/performance/results/<RUN>/report/index.html
```

The status summary records scenario, users, configured duration, average, median, P90, P95, maximum, throughput, JMeter error percentage, and counts/rates for 2xx, 401, 403, 429, 5xx, and other responses. Only actual JTL samples are summarized; the suite does not manufacture results.

## Grafana correlation

During each scenario, capture Grafana evidence for the exact same start/end time as the JMeter run. Use the panels and labels actually available in the deployed ResearchTrack dashboards to observe CPU, memory, request rate, HTTP errors/status codes, and service health. Correlate latency or status changes with those panels. Do not infer missing metrics or invent Prometheus metric names.

## Troubleshooting

### 401 Unauthorized

- Confirm the account credentials are valid, or replace the pre-issued access JWT.
- Check local/Test clocks; JWT validation allows only the backend's configured clock skew.
- The runner refuses a token whose remaining lifetime is shorter than the scenario plus margin. For a 10-minute run, authenticate immediately before running.
- Do not put the refresh token in `PERF_JWT_TOKEN` or in a Bearer header.
- A workload 401 is reported; the suite does not enter an infinite refresh loop.

### 403 Forbidden

- The selected account cannot read `PERF_PROJECT_ID`, or its Student/Supervisor role does not satisfy the route policy.
- Use an authorized dedicated test account; do not weaken authorization.

### 429 Too Many Requests

- Inspect `status-summary.md` and the gateway/Grafana evidence.
- Remember that the gateway general limit is shared by the client IP. Realistic 2–5 second pacing reduces artificial bursts but does not bypass the configured limit.
- PERF-005 intentionally explores this protection behavior. Do not disable or raise the limiter merely to obtain green results.

### 5xx responses

- Correlate the timestamp with `jmeter.log`, response samples, service logs, health endpoints, and Grafana.
- Separate gateway failures from downstream Project/Jira service failures using existing trace IDs/logging where available.
- Do not count 401/403/429 as 5xx.

### Authentication setup fails

- Login returns tokens only through cookies; there is no access-token JSON field to extract.
- Confirm `Cookie__Secure` matches the HTTP/HTTPS environment and the gateway forwards `Set-Cookie` unchanged.
- The login limiter allows 10 requests/minute per client IP. This suite performs one login per JMeter stage, not one per virtual user.

## Safety

- Run moderate/higher/stress only against an environment you are authorized to load-test.
- Prefer a dedicated account and project containing representative synchronized data.
- Never commit a JWT, password, refresh token, OAuth secret, cookie jar, JTL, or generated report.
- Do not edit production authentication, token lifetimes, rate limits, or Jira behavior to make the performance report look better.
