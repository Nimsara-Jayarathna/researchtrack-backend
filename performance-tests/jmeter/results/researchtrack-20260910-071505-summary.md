# ResearchTrack JMeter Test Report

- Generated: 2026-09-10T07:15:16+05:30
- Target: `https://test.api.researchtrack.blipzo.xyz:443`
- Result file: `/Users/dev/Desktop/z/researchtrack-backend/performance-tests/jmeter/results/researchtrack-20260910-071505.jtl`
- Threads: 1
- Ramp-up: 1 seconds
- Duration cap: 60 seconds
- Loops: 1
- Result: **PASS — all HTTP assertions passed**

## Overall HTTP Results

| Samples | Passed | Failed | Error % | Average | Median | Min | Max | P90 | P95 | P99 | Requests/sec | Received KB/sec | Sent KB/sec |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 3 | 3 | 0 | 0.00 | 759 ms | 417 ms | 369 ms | 1490 ms | 1490 ms | 1490 ms | 1490 ms | 0.564 | 0.556 | 0.295 |

## Endpoint Results

| Label | Samples | Passed | Failed | Error % | Average | Min | Max | P90 | P95 | P99 | Requests/sec |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `POST /api/v1/auth/login` | 1 | 1 | 0 | 0.00 | 1490 ms | 1490 ms | 1490 ms | 1490 ms | 1490 ms | 1490 ms | 0.671 |
| `GET /api/v1/auth/me` | 1 | 1 | 0 | 0.00 | 417 ms | 417 ms | 417 ms | 417 ms | 417 ms | 417 ms | 2.398 |
| `GET /api/v1/projects` | 1 | 1 | 0 | 0.00 | 369 ms | 369 ms | 369 ms | 369 ms | 369 ms | 369 ms | 2.710 |

## Transaction Results

| Transaction | Samples | Passed | Failed | Error % | Average |
|---|---:|---:|---:|---:|---:|
| `Login` | 1 | 1 | 0 | 0.00 | 2480 ms |
| `Authenticated project read journey` | 1 | 1 | 0 | 0.00 | 3812 ms |

> No formal project performance acceptance threshold was found. PASS/FAIL above reflects HTTP and assertion success only.
