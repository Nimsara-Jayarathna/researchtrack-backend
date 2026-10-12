# TASK 10 — Jira Issues API performance investigation

Date: 2026-10-12. Working branch: `integration/main-kafka-production-20261011`.
Base commit: `1810f09535d6a6d00947bcf84d18fde473e40c39`, with existing local TASK 02–09 changes preserved.

## Outcome

The cause of the observed **production** 2.7–3.8-second p95 is **NOT ESTABLISHED**.
The read path contains no Kafka operation, per-issue database query, Atlassian request,
or retry loop. Actual isolated MySQL plans use the existing indexes.

The confirmed scaling concern is the full, unpaginated issue snapshot, including
descriptions: 1,000 synthetic issues returned approximately 2.45 MB and 5,000 returned
12.28 MB. Larger snapshots and concurrency increased local request latency. This
does not isolate serialization from EF materialization, network transfer or GC,
and does not prove which component delayed the production requests.

**No application optimization was applied.** A query/index/cache/response change is
not supported by a confirmed production root cause. This task adds a reproducible
local measurement harness and an application-team handoff. Before/after optimization
metrics are **NOT APPLICABLE**, because the application implementation is unchanged.

## Endpoint and implementation trace

`GET /api/v1/projects/{projectId:guid}/jira/issues`, authenticated, read-only:

| Stage | Implementation | Behavior |
|---|---|---|
| Gateway | `src/Gateway/ResearchTrack.Gateway/appsettings.json`, `project-jira-route`, cluster `jira`; `Program.cs` | `/api/v1/projects/{projectId}/jira/{**catch-all}` forwards to Jira. Normal general rate limit is 120/minute/client IP. |
| Controller | `src/Services/ResearchTrack.JiraService/Controllers/ProjectJiraIssuesController.cs`, `Issues` | `[Authorize]`; calls the query service and returns the existing success envelope. |
| Authorization | `Infrastructure/ProjectAuthorizationClient.cs`, `EnsureCanAccessAsync` | Forwards caller authentication to Project Service with GET `/api/v1/projects/{id}/authorization/access`. A successful response allows the read. Timeout/transport failure maps to 503; denial blocks Jira database access. |
| Project authorization | `src/Services/ResearchTrack.ProjectService/Controllers/ProjectsController.cs`, `VerifyReadAccess`; `Features/Projects/ProjectService.cs`, `CanAccessAsync` | Supervisor ownership or student membership is checked using an asynchronous `AnyAsync` query. This hop is required. |
| Query service / data access | `Features/JiraIssueQueryService.cs`, `GetIssuesAsync`; `Persistence/JiraDbContext.cs` | No separate repository abstraction. EF directly runs one no-tracking project connection lookup and one no-tracking, project-filtered issue projection ordered by `IssueKey`. |
| Response | `Contracts/JiraContracts.cs`, `JiraIssueListResponse` / `JiraIssueResponse` | All stored issues, 15 issue fields including `DescriptionJson`, status summary, and persisted sync metadata; ASP.NET JSON serialization and Gateway transfer follow. |

## Investigation findings

| Concern | Evidence and conclusion |
|---|---|
| N+1 | Every timed successful request issued exactly **two Jira SQL reader commands**, independent of 100/1,000/5,000 issues. Production also performs the separate Project authorization query; that service is stubbed in this benchmark. No per-issue read loop found. |
| Missing/unused index | Real migrated schema: connection unique project index and issue `(ResearchProjectId, IssueKey)` index were used. No missing index demonstrated. Other indexes serve different operations; do not remove them from this evidence. |
| Inefficient joins | The two Jira read queries contain no joins. |
| Pagination / payload | No pagination or row cap. Descriptions are buffered with all projected issues by `ToListAsync`. The payload and cost grow with stored issue count. |
| Transformations | Status categories are collected and counted in several linear passes. No evidence these passes dominate latency; no micro-optimization applied. |
| External API calls | No Atlassian/provider call on this route. There is one required internal Project authorization HTTP call. |
| Blocking / retries | Awaited HTTP/EF operations; no `.Result`, `.Wait`, Kafka wait, or application retry delay found in this read path. Project HTTP timeout is configured through `Jira:ProjectServiceTimeoutSeconds`; the test configuration uses 10 seconds. |
| Query plans | Captured **actual EF SQL**, parameterized with synthetic project IDs, was examined with `EXPLAIN ANALYZE` and `EXPLAIN FORMAT=JSON`; details below. |
| Connections | Local MySQL maximum connections 64, connector pool limit 32, measured concurrency 1 and 4. No observed HTTP/query failure at those loads. Connection acquisition time was not independently measured; production pool saturation, TLS handshakes or DB pressure remain unverified. |
| Kafka relationship | No direct Kafka dependency in the read implementation. Possible shared CPU/I/O/database contention with background processing is not excluded by an isolated benchmark with workers disabled. |

Limiting result sets and measuring actual query plans are appropriate next steps when
large responses are demonstrated; pagination requires an explicit contract decision.
See [EF Core efficient querying](https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying)
and [MySQL EXPLAIN](https://dev.mysql.com/doc/refman/8.4/en/explain.html).

## Existing production evidence — historical, not rerun

The local audit artifact `artifacts/final-devops-hardening/production-k6-evidence.json`
and `FINAL_DEVOPS_HARDENING.md` record run `38147705025`, artifact `11705509313`:

| Historical measurement | Result |
|---|---|
| Jira issues | 9 HTTP 2xx responses; average 1,921.51 ms; p95 3,076.93 ms |
| GitHub activity | 8 HTTP 2xx responses; p95 375.95 ms |
| Overall business requests | 61 requests; p95 1,566.86 ms |
| Applied gates | Overall smoke p95 3,000 ms; endpoint threshold map `{}` |
| Earlier Jira sample, run `38135611889` | 8 requests; p95 3,755.43 ms, from the earlier audit |

These small samples do not identify a root cause or establish a production SLO.
No fresh production requests, database queries, workflow dispatches or GitHub variable
reads were performed. The current live value of the endpoint-threshold variable is
**NOT VERIFIED**; an older artifact is not proof of current configuration.

## Local measurement method

Runner: `scripts/performance/measure-jira-issues-local.sh`.
Harness: `tests/performance/jira-issues/`.

- macOS arm64, .NET SDK 10.0.400; actual Gateway and Jira applications hosted by the
  existing `ResearchTrackWebApplicationFactory` using Kestrel/YARP over loopback HTTP.
- New runner-owned MySQL 8.4 container, 2 CPU limit, 1 GiB memory limit, 128 MiB
  InnoDB buffer pool, maximum 64 connections; actual Jira migrations applied to a
  fresh synthetic database, connector pool limited to 32. MySQL transport uses TLS
  (`SslMode=Required`); this is not a production certificate-verification test.
- Three projects share the database: 100, 1,000 and 5,000 issues (6,100 total).
  Description text is 1 KiB for the small case and 2 KiB for the two larger cases;
  descriptions are synthetic JSON, other nullable fields mostly unset, statuses
  rotate between new/indeterminate/done. No real Jira/provider/user data used.
- Actual JWT middleware, authentication forwarding and `ProjectAuthorizationClient`
  are exercised. Its HTTP transport returns synthetic 204/403 responses in process;
  a real Project Service/database/network hop is **not** measured.
- Kafka is disabled by the existing test factory and application hosted workers are
  removed only from the test hosts. The fixture overrides the Gateway global limiter
  only in test DI so the normal 120/minute bucket does not mask capacity with 429s.
  Production source, limits and performance-exemption policy are unchanged.
- Only `127.0.0.1:13308` (DB), `:19114` (Jira) and `:19115` (Gateway) are used. The
  runner rejects occupied ports, remote Docker contexts/hosts and unexpected fixture
  identity. The harness validates the database target and both HTTP listeners.
- Each size/concurrency pair has **three runs × 200 measured requests**, with 20
  warm-up requests before each baseline run. Concurrency is 1 or 4 in a closed loop,
  not an arrival-rate capacity test. A separate 100-ms synthetic authorization delay
  has three sequential runs × 200 requests.
- Two successful disposable invocations: **8,400 timed requests total**, including
  1,200 authorization-delay requests, plus 720 baseline warm-ups and contract/auth
  preflight checks. No observed measured request failed. Counts are not xUnit cases.
- Timing starts before HTTP send and ends after reading the entire response body.
  These native HttpClient measurements are **not a k6 run** or identical to k6's
  `response.timings.duration`. Native k6 is not installed here. Percentiles use
  linear interpolation independently for each run; p95 values are not averaged.
- Service timing covers the existing query method, including authorization and EF
  materialization/summary. `ReaderOpenMs` measures command execution up to reader
  availability, **not** draining every row, pooling, or the entire DB operation.
  `AuthMs` measures only the synthetic transport. Instrumentation adds some overhead.

This is a representative **local scaling matrix**, not a matched production dataset
or topology. Gateway, Jira and the load generator share one .NET process, so shared
GC and client body-copy costs can affect results; Azure uses separate application
instances. Cold starts, large production descriptions, real authorization latency,
background writes, network bandwidth, CPU/GC distributions and production data skew
were not reproduced. Aggregate timings alone do not separate these costs.

## Baseline results

Final invocation `rt-jira-perf-8afbddafb675`; all times in milliseconds. Each cell lists
the three independent 200-request runs, not a pooled percentile.

| Issues | Concurrency | Approximate body bytes | p95: runs 1 / 2 / 3 | p99: runs 1 / 2 / 3 |
|---|---|---|---|---|
| 100 | 1 | 143,231 | 9.779 / 10.284 / 10.534 | 18.190 / 17.076 / 20.893 |
| 100 | 4 | 143,231 | 22.514 / 22.363 / 22.427 | 56.372 / 33.330 / 27.192 |
| 1,000 | 1 | 2,454,835 | 46.768 / 50.950 / 42.246 | 54.184 / 69.807 / 55.096 |
| 1,000 | 4 | 2,454,835 | 120.100 / 213.681 / 107.214 | 135.983 / 241.499 / 144.728 |
| 5,000 | 1 | 12,277,501 | 153.874 / 186.388 / 174.652 | 170.073 / 281.542 / 232.768 |
| 5,000 | 4 | 12,277,501 | 499.846 / 746.990 / 513.704 | 525.725 / 792.737 / 564.141 |
| 1,000; auth delay 100 ms | 1 | 2,454,835 | 173.521 / 172.172 / 173.117 | 185.052 / 180.887 / 177.997 |

Response metadata can vary by a byte. The first successful invocation,
`rt-jira-perf-d5ac136bb159`, measured 1,000/concurrency-4 p95 at 89.468–122.273 ms and
5,000/concurrency-4 at 515.046–806.567 ms. Both invocations retained all issues and
description fields. Variation is visible and is a reason to retain repeated runs.

Controlled sensitivity comparison: normal 1,000-issue sequential p95 was 42.246–50.950
ms; adding 100 ms inside the synthetic authorization transport produced 172.172–173.521
ms p95, with transport p95 102.514–102.732 ms. This demonstrates that authorization
latency propagates into the read, **not that it caused the production incident**.
It is not an optimization before/after comparison; subtracting unrelated percentiles
does not provide an exact phase decomposition.

### Actual query plans

Final fixture, captured generated SQL, one warm plan execution per query/project:

| Query | Actual plan evidence |
|---|---|
| Connection lookup | `const`, unique `IX_jira_connections_ResearchProjectId`, one row, `LIMIT 2`; no connection table scan. |
| Issues: 100 | `ref`, `IX_jira_issues_project_key`, no filesort; actual 100 rows, approximately 0.422 ms through the iterator. |
| Issues: 1,000 | Same index/order; actual 1,000 rows, approximately 3.19 ms. |
| Issues: 5,000 | Same index/order; actual 5,000 rows, approximately 20.8 ms. Estimated 2,505 rows differed from actual 5,000, but the useful indexed plan was retained. |

Historical migrations name the issue index `IX_jira_issues_project_key`; model-created
fixtures may name the equivalent index `IX_jira_issues_ResearchProjectId_IssueKey`.
Do not treat this naming difference as a missing index. `EXPLAIN ANALYZE` executes
the captured SELECT in the disposable database; its iterator time excludes the
full application's materialization/serialization/client transfer. It is not the
endpoint latency or a production plan. No `ANALYZE TABLE` or index DDL was performed.

## Tests and evidence

| Check executed for TASK 10 | Actual result |
|---|---|
| Final Release build of benchmark and referenced Jira/Gateway/testing projects | **PASS**, 0 warnings, 0 errors |
| Disposable benchmark, two complete invocations | **PASS**, 4,200 timed requests each; 21 groups each |
| Final HTTP response preflights | **PASS**, HTTP 200 success envelope, expected issue/summary counts, all keys ordered, 15 projected fields and complete synthetic descriptions, sync status preserved |
| HTTP access denial and invalid JWT | **PASS**, 403 before Jira SQL and 401 before authorization transport |
| Query/auth counts | **PASS**, exactly two Jira SQL commands and one authorization call per timed request |
| Actual generated SQL plans | **PASS** execution, six ANALYZE plans each invocation; six additional JSON plans in final invocation; existing indexes used |
| Runner disabled / remote DB configuration guards | **PASS**, both rejected before Docker/database access |
| Jira Release regression suite, excluding `KafkaIntegration` and `DatabaseIntegration` categories | **PASS**, 106 passed, 0 failed, 0 skipped |
| Gateway `PerformanceRateLimitPolicyTests`, Release | **PASS**, 14 passed, 0 failed, 0 skipped |
| Existing `tests/performance/test-report.sh` | **PASS**, synthetic Node k6 report/runtime endpoint-threshold regression; Node emitted an existing module-type warning |
| `bash -n` and ShellCheck, new runner | **PASS** |
| Native k6 workload | **NOT EXECUTED**, k6 not installed; Node formatter test is not a k6 load test |
| Production/staging matched baseline, production plans, real Project auth timing | **NOT EXECUTED** |
| Fresh full Kafka integration or historical MySQL upgrade suite | **NOT EXECUTED for TASK 10**; application Kafka/migration code unchanged; fresh fixture migrated for this benchmark |

Local ignored evidence:

- `artifacts/jira-issues-performance/rt-jira-perf-8afbddafb675/`: `measurements.json`,
  `explain-analyze.json`, `benchmark.log`, `source-and-runtime.json`, `guard-checks.txt`,
  `fixture-location.txt`.
- `artifacts/jira-issues-performance/rt-jira-perf-d5ac136bb159/`: initial successful
  benchmark measurements, ANALYZE plans and log.
- `artifacts/jira-issues-performance/regression/`: Jira/Gateway TRX and Release build log.

Four earlier harness-startup attempts failed before producing performance evidence:
invalid local SSL enum, initialization-server readiness race, content-root resolution,
and a duplicated local HTTP listener. Those **FAIL** logs remain in separate fixture
artifact directories; they are not counted as successful tests or latency samples.
The runner now waits for the final MySQL TCP server/fixture account and configures
explicit content roots and single loopback listeners. All runner-owned containers
are stopped, with named data volumes retained. No production resources were accessed.
The two successful MySQL containers exited 137 during the runner's default-timeout
cleanup, with `OOMKilled=false`; benchmark execution had already completed successfully.
Clean database shutdown/restart durability is not claimed by these performance tests.

## Endpoint SLO recommendation

`K6_ENDPOINT_P95_THRESHOLDS_JSON` uses operation key **`jira_issues`**, not the URL.
`tests/performance/lib/profiles.js` creates a distinct
`researchtrack_op_jira_issues_duration` threshold `p(95)<value` for profiles where
the operation is active. Enablement of integration reads depends on
`K6_INCLUDE_INTEGRATIONS=true`. The endpoint gate supplements the overall profile
gate. Its metric comes from `response.timings.duration` in `lib/metrics.js`.
The existing synthetic formatter regression proved overall and Jira targets remain
distinct. A failed latency threshold should remain a failed check; see
[k6 thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/).

For **this lab workload only**, propose Jira Issues p95 **<1,000 ms**, zero unsuccessful
responses, with warm processes, at most 5,000 issues, approximately 2 KiB descriptions
and at most four concurrent requests. The worst observed repeated-run p95 was
806.567 ms. This is a provisional regression budget with limited headroom, not an
approved production SLO, sustained-throughput guarantee or currently enforced gate.

A numeric **production** endpoint SLO remains **PENDING REPRESENTATIVE BASELINE**.
The lab target must not be copied into GitHub variables without matched staging
evidence and owner approval. The current unbounded API has no maximum payload cohort,
so one scalar target cannot promise the same experience for arbitrarily large projects.
The README's `jira_issues:3000` example is not approval or evidence either.

Before selecting the production number, baseline a production-equivalent staging
topology and real Project authorization with sanctioned synthetic/anonymized data
matching typical and upper-percentile project counts/description sizes. Retain at
least three runs per cohort, at least 200 measured endpoint requests per run, expected
concurrency/arrival rate and a separate cold-start cohort. Increase sample size or
duration if the tails vary; these minima alone do not establish statistical certainty.
Record failures/429s as well as latency, response size, resource pressure and dropped
iterations. After agreement, the operator can add the reviewed millisecond target
under `jira_issues` while preserving existing map entries. No threshold or live variable
was changed in TASK 10, and existing thresholds were not relaxed.

## Developer handoff and optimization decision

Owner: Jira API team, with Project Service and database/SRE assistance.

1. Reproduce using an approved staging project whose issue-count and description-size
   distribution matches the slow cohort. Run the existing k6 read profile with
   integrations active and retain per-endpoint samples across multiple runs. Do not
   run a deployed workflow automatically as part of this local investigation.
2. Correlate request IDs with timings for Gateway forwarding, Project authorization
   HTTP/Project query, MySQL connection acquisition, connection lookup, complete issue
   enumeration, summary and response serialization/transfer. Collect CPU/GC, pool/DB
   connections and background-job load without logging bearer tokens, protected
   provider tokens, user descriptions or SQL parameter values from real data.
3. Capture generated SQL and plans **on staging** with realistic data distribution.
   Compare row estimates with actual rows and existing index usage. Do not run
   production DDL, index changes, `ANALYZE TABLE` or unbounded live plan executions
   from this procedure.
4. If large snapshots dominate, agree a versioned or backward-compatible paginated
   list/detail design and summary semantics with frontend/product owners. Preserve
   the existing route/contract until that decision; silently removing descriptions,
   truncating issues or applying a row cap would change behavior.
5. If another phase dominates, address that measured phase. Do not bypass access
   checks or introduce speculative cross-user caching. Narrowing the connection
   projection and consolidating status passes are possibilities only if measured
   costs justify them. There is no demonstrated need for another issue index.
6. For an approved minimal fix, rerun the same cohorts before/after with unchanged
   gates and resources; cover authorization, project isolation, ordering, description
   content, summaries, sync metadata and DateTimeOffset precision. Retain evidence
   of both improvement and tail/failure behavior. Deployment requires separate review.

## Reproduce safely on macOS

Prerequisites: Docker Desktop with a local Unix-socket context, .NET SDK pinned by
`global.json`, Python 3 and OpenSSL. NuGet/image downloads may require network access.
Use this worktree; do not source production `.env` files. Keep the host otherwise idle
when collecting comparative latency evidence.

```bash
cd /Users/dev/Desktop/z/researchtrack-backend-integration-main-kafka
./scripts/performance/measure-jira-issues-local.sh
```

The runner refuses occupied fixed ports; free them by inspecting their existing owner,
not by stopping unrelated containers. Expect `PASS` preflight checks, 21 measurement
rows, exit 0 and a `RETAINED` message with a unique evidence directory. Treat nonzero
exit or missing measurements/plans as FAIL, not a successful baseline. Inspect the
private benchmark log for diagnostics; initialization waits are bounded to 180 seconds
and each HTTP operation to 30 seconds. Initial image pull/build may take longer.

Each invocation creates new storage, applies migrations and seeds only that new
fixture. Cleanup stops only its identified MySQL container; it does not delete the
container, volumes or data. The private temporary directory retains fixture credentials
with owner-only access; do not publish it. Retained named volumes consume disk space
and need an independently reviewed cleanup decision. Subsequent invocations use new
fixtures, never overwrite existing rows, and require no production credentials.

Regression commands used:

```bash
dotnet build tests/performance/jira-issues/JiraIssuesBenchmark.csproj -c Release
dotnet test tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj \
  -c Release --filter 'Category!=KafkaIntegration&Category!=DatabaseIntegration'
dotnet test tests/ResearchTrack.Gateway.Tests/ResearchTrack.Gateway.Tests.csproj \
  -c Release --filter FullyQualifiedName~PerformanceRateLimitPolicyTests
./tests/performance/test-report.sh
bash -n scripts/performance/measure-jira-issues-local.sh
shellcheck scripts/performance/measure-jira-issues-local.sh
```

## Exact TASK 10 file changes

All four files are new; no runtime application, migration, workflow, threshold or
configuration file was changed for TASK 10:

1. `scripts/performance/measure-jira-issues-local.sh` — guarded disposable runner.
2. `tests/performance/jira-issues/JiraIssuesBenchmark.csproj` — standalone harness,
   references existing application and testing projects; not added to the solution.
3. `tests/performance/jira-issues/Program.cs` — real HTTP/EF measurements, synthetic
   data/auth, contract guards and captured query plans; test-only instrumentation.
4. `docs/devops/JIRA_ISSUES_PERFORMANCE_INVESTIGATION.md` — this report and handoff.

Existing TASK 02–09 local changes remain intact. No commit, staging, push, merge,
deployment, production database modification or GitHub/Azure setting change performed.
