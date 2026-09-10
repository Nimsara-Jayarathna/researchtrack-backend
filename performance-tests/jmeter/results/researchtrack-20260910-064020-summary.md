# ResearchTrack JMeter Test Report

- Generated: 2026-09-10T06:40:29+05:30
- Target: `https://test.api.researchtrack.blipzo.xyz:443`
- Result file: `/Users/dev/Desktop/z/researchtrack-backend/performance-tests/jmeter/results/researchtrack-20260910-064020.jtl`
- Threads: 1
- Ramp-up: 1 seconds
- Duration cap: 60 seconds
- Loops: 1
- Result: **FAIL — one or more HTTP requests failed**

## Overall HTTP Results

| Samples | Passed | Failed | Error % | Average | Median | Min | Max | P90 | P95 | P99 | Requests/sec | Received KB/sec | Sent KB/sec |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 | 0 | 3 | 100.00 | 654 ms | 414 ms | 221 ms | 1326 ms | 1326 ms | 1326 ms | 1326 ms | 0.611 | 0.442 | 0.142 |

## Endpoint Results

| Label | Samples | Passed | Failed | Error % | Average | Min | Max | P90 | P95 | P99 | Requests/sec |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/auth/login` | 1 | 0 | 1 | 100.00 | 1326 ms | 1326 ms | 1326 ms | 1326 ms | 1326 ms | 1326 ms | 0.754 |
| `GET /api/v1/auth/me` | 1 | 0 | 1 | 100.00 | 414 ms | 414 ms | 414 ms | 414 ms | 414 ms | 414 ms | 2.415 |
| `GET /api/v1/projects` | 1 | 0 | 1 | 100.00 | 221 ms | 221 ms | 221 ms | 221 ms | 221 ms | 221 ms | 4.525 |

## Transaction Results

| Transaction | Samples | Passed | Failed | Error % | Average |
|---|---:|---:|---:|---:|---:|
| `Login` | 1 | 0 | 1 | 100.00 | 2161 ms |
| `Authenticated project read journey` | 1 | 0 | 1 | 100.00 | 3569 ms |

## Failed Requests

| Label | Status | Response | Assertion/Error |
|---|---:|---|---|
| `POST /api/v1/auth/login` | 401 | Unauthorized | Test failed: code expected to equal / ****** received : [[[401]]] ****** comparison: [[[200]]] / |
| `GET /api/v1/auth/me` | 401 | Unauthorized | Test failed: code expected to equal / ****** received : [[[401]]] ****** comparison: [[[200]]] / |
| `GET /api/v1/projects` | 401 | Unauthorized | Test failed: code expected to equal / ****** received : [[[401]]] ****** comparison: [[[200]]] / |

> No formal project performance acceptance threshold was found. PASS/FAIL above reflects HTTP and assertion success only.
