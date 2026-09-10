# ResearchTrack JMeter performance tests

This is an isolated, read-only Apache JMeter setup. It does not change the application, schema, API, or existing test suites.

## Project discovery

ResearchTrack is a .NET 10 / ASP.NET Core microservice backend using EF Core and MySQL, with YARP as its public API gateway. The local gateway is `http://localhost:5000`; its route prefix is `/api/v1`. Health endpoints are available at `/health/live` and `/health/ready` on each service.

The included plan exercises the gateway's existing authenticated read flow:

1. `POST /api/v1/auth/login` with `{"email":"...","password":"..."}`
2. `GET /api/v1/auth/me`
3. `GET /api/v1/projects`
4. `GET /api/v1/projects/{id}` only when the user has at least one accessible project

The project list response is `ApiResponse<T>` JSON (`success`, `data`, `error`, `meta`). A JSON Extractor captures `$.data[0].id` as `project_id`; the detail call is skipped for users with no projects.

## Authentication

Login returns a `200` JSON body containing the user, but **not** an access token field. The server sends HTTP-only session cookies instead:

- `ss_access_token`, scoped to `/api`
- `ss_refresh_token`, scoped to `/api/v1/auth`

Consequently the plan uses JMeter's HTTP Cookie Manager, which captures and sends these cookies automatically. It intentionally does not invent an `Authorization: Bearer` header or a JSON token extractor. Each virtual user logs in once, then reuses its session cookie for read requests.

## Included JMeter components

- Configurable Thread Group, duration cap, loops, and ramp-up
- HTTP Request Defaults, JSON Header Manager, Cookie Manager, and Cache Manager
- CSV Data Set Config for separate credentials per virtual user
- Transaction Controllers for login and the project journey
- JSON Extractor for the first accessible project ID
- HTTP-status and JSON-success assertions on every included endpoint
- 500–1000 ms configurable random think time

The plan deliberately has no memory-heavy GUI listeners such as View Results Tree. Run it in non-GUI mode only.

## Prerequisites and setup

1. Install Java and Apache JMeter **5.6 or later** and ensure `jmeter` is on `PATH`. On Windows, set `JMETER_BIN` to the full `jmeter.bat` path if necessary.
2. Start the existing backend from the repository root, for example:

   ```bash
   ./scripts/start-all.sh
   ./scripts/health.sh core
   ```

   The runner checks `http://localhost:5000/health/live` before it starts. It must return HTTP 200; a 401/403 means port 5000 is not the expected running gateway or its configuration is incomplete.

3. Create a local CSV from the example. Do not commit it:

   ```bash
   cd performance-tests/jmeter
   cp data/users.csv.example data/users.csv
   ```

4. Replace the example row with dedicated, non-production test accounts. The file has exactly this header:

   ```csv
   email,password
   ```

Use enough distinct accounts for the requested thread count where possible. Do not put passwords, tokens, or API keys in version control.

If the target is HTTPS, set `PROTOCOL=https`. This is required when the server sets secure authentication cookies.

## Run profiles

All commands below execute in non-GUI mode and create a timestamped `.jtl`, JMeter log, and Markdown summary under `results/`. The summary is also printed automatically when the run ends. The runner does not generate an HTML dashboard.

### Smoke test

```bash
cd performance-tests/jmeter
THREADS=1 RAMPUP=1 DURATION=60 LOOPS=1 ./run-test.sh
```

### Normal load test

The gateway limits login requests to 10 per minute per source IP. Because every virtual user must first log in, use a sufficiently long ramp-up. This profile has 25 users spread over three minutes:

```bash
THREADS=25 RAMPUP=180 DURATION=600 LOOPS=-1 ./run-test.sh
```

### Peak load test

This is a **profile example**, not a statement of capacity. Its ten-minute ramp-up avoids treating the login limiter as an application performance failure:

```bash
THREADS=100 RAMPUP=600 DURATION=1200 LOOPS=-1 ./run-test.sh
```

### Stress test

Run increasing concurrency intentionally and record each report separately. Use at least six seconds of ramp-up per user to respect the current login limiter:

```bash
for users in 50 100 200 300; do
  THREADS="$users" RAMPUP="$((users * 6))" DURATION=1800 LOOPS=-1 ./run-test.sh
done
```

At higher rates, `429 Too Many Requests` on login is expected behavior from the gateway's configured limiter, not a successful application request. Do not hide it or exclude it from the error rate.

## Configuration

`run-test.sh` and `run-test.bat` accept these environment variables and pass them to JMeter as properties:

| Variable | Default | Purpose |
|---|---:|---|
| `PROTOCOL` | `http` | Target protocol |
| `HOST` | `localhost` | Gateway host |
| `PORT` | `5000` | Gateway port |
| `THREADS` | `1` | Virtual users |
| `RAMPUP` | `1` | Seconds to start all users |
| `DURATION` | `60` | Scheduler duration cap in seconds |
| `LOOPS` | `1` | Iterations per user; use `-1` with a duration for repeated traffic |
| `USERS_FILE` | `data/users.csv` | Credential CSV path |
| `THINK_TIME_MS` | `500` | Minimum think time in milliseconds |
| `THINK_TIME_JITTER_MS` | `500` | Additional random think time |
| `JMETER_BIN` | `jmeter` | JMeter executable |

For direct JMeter use, the equivalent smoke command is:

```bash
jmeter -n -t performance-tests/jmeter/test-plan.jmx -l performance-tests/jmeter/results/smoke.jtl -j performance-tests/jmeter/results/smoke.log -Jsummariser.ignore_transaction_controller_sample_result=false -Jprotocol=http -Jhost=localhost -Jport=5000 -Jthreads=1 -Jrampup=1 -Jduration=60 -Jloops=1 -Jusers_file=performance-tests/jmeter/data/users.csv
```

## Results and reports

- `.jtl` raw samples: `results/researchtrack-<timestamp>.jtl`
- JMeter logs: `results/jmeter-<timestamp>.log`
- Markdown reports: `results/researchtrack-<timestamp>-summary.md`

The automatic report includes sample counts, successes, failures, error percentage, average/median/p90/p95/p99 response times, throughput, byte rates, endpoint results, transaction results, and failed-request details.

Example acceptance thresholds only—agree the real SLO/SLA with the product and operations teams:

- Error rate: under 1%
- p95 response time: under 1000 ms
- p99 response time: under 2000 ms

An HTTP response that arrives quickly but fails its status or JSON assertions is reported as an error.

## Safety

The supplied plan performs no create, update, delete, registration, password, or refresh-token operations. Run high-load tests only against an approved environment, with explicit authorization, monitoring, rollback plans, and capacity owners aware. Do not run peak or stress profiles against production by default.
