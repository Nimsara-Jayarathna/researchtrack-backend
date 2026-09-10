# ResearchTrack JMeter Performance Test Report

## 1. Test Information

- Date/time: 2026-09-09 22:45:45 IST (Asia/Colombo)
- Requested environment: `https://test.researchtrack.blipzo.xyz/`
- API gateway used by the deployed frontend: `https://test.api.researchtrack.blipzo.xyz/`
- JMX test plan: `performance-tests/jmeter/test-plan.jmx`
- JMeter version: Apache JMeter 5.6.3
- Java version: OpenJDK 21.0.11
- Branch actually checked out: `test/sprint-01-qa-automation-testing`
- Requested branch name: `feature/sprint-01-qa-automation-testing` (not checked out)
- JMeter process exit status: `0`
- Performance assessment: **INCONCLUSIVE** because authentication test data was invalid
- QA execution result: **FAIL — test-data/authentication blocker**

The requested site URL was reachable with HTTP 200, but it serves the React frontend. Its deployed JavaScript identifies `https://test.api.researchtrack.blipzo.xyz` as the API base URL. A diagnostic run against the frontend host returned 405 for login and HTML rather than API JSON for GET requests. That run was preserved separately and the existing JMX was retried against the discovered API gateway.

## 2. Test Configuration

- Threads/users: 1
- Ramp-up: 1 second
- Loop count: 1
- Duration cap: 60 seconds
- Think time: 500 ms plus 0–500 ms random jitter
- Protocol: HTTPS
- API target host: `test.api.researchtrack.blipzo.xyz`
- Port: 443
- Connect timeout: 10,000 ms
- Response timeout: 30,000 ms
- Credential source: `performance-tests/jmeter/data/users.csv` (values redacted)
- Authentication: one login per virtual user; HTTP-only cookies reused by the Cookie Manager

The workload is a safe, read-only smoke profile. The plan does not create, update, or delete application data.

Endpoints in the plan:

1. `POST /api/v1/auth/login`
2. `GET /api/v1/auth/me`
3. `GET /api/v1/projects`
4. `GET /api/v1/projects/{project_id}` when an accessible project ID is extracted

## 3. Overall Results

The dashboard's `Total` row counts HTTP request samples and excludes the two generated Transaction Controller parent samples. The JTL contains five rows in total: three HTTP samples and two parent transaction samples.

| Metric | Actual result |
|---|---:|
| HTTP request samples | 3 |
| Transaction parent samples | 2 |
| Successful HTTP requests | 0 |
| Failed HTTP requests | 3 |
| Error percentage | 100.00% |
| Average response time | 889 ms |
| Median response time | 1,146 ms |
| Minimum response time | 237 ms |
| Maximum response time | 1,284 ms |
| P90 | 1,284 ms |
| P95 | 1,284 ms |
| P99 | 1,284 ms |
| Throughput / requests per second | 0.547 req/s |
| Received throughput | 0.412 KB/s |
| Sent throughput | 0.123 KB/s |

These timings describe failed validation/authorization responses and must not be interpreted as successful application performance.

## 4. Endpoint Results

| Endpoint/Test Label | Samples | Success | Failures | Error % | Average | Min | Max | P90 | P95 | P99 | Throughput |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/auth/login` | 1 | 0 | 1 | 100.00% | 1,146 ms | 1,146 ms | 1,146 ms | 1,146 ms | 1,146 ms | 1,146 ms | 0.873 req/s |
| `GET /api/v1/auth/me` | 1 | 0 | 1 | 100.00% | 237 ms | 237 ms | 237 ms | 237 ms | 237 ms | 237 ms | 4.219 req/s |
| `GET /api/v1/projects` | 1 | 0 | 1 | 100.00% | 1,284 ms | 1,284 ms | 1,284 ms | 1,284 ms | 1,284 ms | 1,284 ms | 0.779 req/s |
| `GET /api/v1/projects/{project_id}` | 0 | 0 | 0 | N/A | N/A | N/A | N/A | N/A | N/A | N/A | N/A |

The project-detail sampler was correctly skipped because no project ID was available after the unauthenticated project-list response.

Transaction Controller results:

| Transaction | Samples | Success | Failures | Error % | Average | Min | Max | P90 | P95 | P99 | Throughput |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Login | 1 | 0 | 1 | 100.00% | 2,077 ms | 2,077 ms | 2,077 ms | 2,077 ms | 2,077 ms | 2,077 ms | 0.481 txn/s |
| Authenticated project read journey | 1 | 0 | 1 | 100.00% | 4,314 ms | 4,314 ms | 4,314 ms | 4,314 ms | 4,314 ms | 4,314 ms | 0.232 txn/s |

## 5. Errors and Failed Requests

| Label | Method | Endpoint | Status | Response/error | Likely reason | Classification |
|---|---|---|---:|---|---|---|
| `POST /api/v1/auth/login` | POST | `/api/v1/auth/login` | 400 | `Bad Request`; API code `VALIDATION_ERROR`: email is not a valid email address; HTTP-200 assertion failed | The CSV's placeholder email has no `@` and has trailing whitespace | Test data / JMeter input configuration |
| `GET /api/v1/auth/me` | GET | `/api/v1/auth/me` | 401 | `Unauthorized`; HTTP-200 assertion failed | Login did not establish a session cookie | Authentication failure cascading from test data |
| `GET /api/v1/projects` | GET | `/api/v1/projects` | 401 | `Unauthorized`; HTTP-200 assertion failed | Login did not establish a session cookie | Authentication failure cascading from test data |

The transaction parents `Login` and `Authenticated project read journey` are also marked failed because their child HTTP samples failed. No HTTP 5xx response, timeout, connection failure, TLS failure, or malformed-JMX exception occurred.

## 6. Performance Observations

No formal project performance acceptance threshold was found. The README contains example thresholds only and explicitly says that real SLO/SLA values must be agreed with product and operations.

- The API gateway and health endpoint were reachable.
- The smoke run remained stable at one virtual user; no connection errors or server errors occurred.
- All measured responses were unsuccessful, so latency, percentile, and throughput values cannot establish performance of the intended authenticated workflow.
- A new run with valid dedicated test credentials is required before assigning a performance PASS, WARNING, or FAIL.

## 7. Issues Found

1. **Test-data failure:** `data/users.csv` contains placeholder values. The email is syntactically invalid, producing HTTP 400.
2. **Target-host mismatch:** the user-facing environment URL serves the frontend; the API gateway is `https://test.api.researchtrack.blipzo.xyz`.
3. **Branch mismatch:** the current branch is `test/sprint-01-qa-automation-testing`, not `feature/sprint-01-qa-automation-testing`.
4. **JMeter console summary anomaly:** the CLI summariser printed zero samples even though the JTL and generated dashboard contain five JTL rows and three HTTP samples. Metrics in this report come from the JTL/dashboard, not that console line.

## 8. Conclusion

**QA result: FAIL due to invalid test data. Performance result: INCONCLUSIVE.**

The existing JMX loaded and executed without a JMeter process error, and the gateway was reachable. The failure is not evidence of an application defect or a performance regression: the supplied account data cannot pass API email validation, so authentication and the intended read journey could not be exercised. Replace the local CSV row with a valid, authorized, non-production test account and rerun the same one-user smoke profile.

## 9. Generated Artifacts

- `performance-tests/jmeter/results/latest/results.jtl`
- `performance-tests/jmeter/results/latest/jmeter.log`
- `performance-tests/jmeter/results/latest/JMETER_TEST_SUMMARY.md`
- Preserved frontend-host diagnostic run: `performance-tests/jmeter/results/diagnostic-frontend-host-20260909-224426/`
