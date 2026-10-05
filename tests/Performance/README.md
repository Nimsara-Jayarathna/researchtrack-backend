# ResearchTrack k6 performance tests

These tests exercise a **deployed ResearchTrack environment** through the public Gateway. They are intentionally separate from the .NET unit/integration suite.

## Profiles

| Script | Default profile | Purpose |
|---|---|---|
| `smoke.js` | 5 VUs for 15 seconds | Fast deployed-environment regression gate |
| `load.js` | 20 VUs with ramp/hold/ramp-down | Expected-load behaviour |
| `stress.js` | ramps to 50 VUs | Controlled saturation/regression investigation |
| `webhook-spike.js` | 25 VUs for 20 seconds | Signed GitHub `ping` webhook ingress spike |

The normal smoke threshold is deliberately tolerant: `p95 < 500 ms`, `<1%` HTTP failures, and `>99%` checks. Tighten it only after collecting a stable baseline from the actual Azure environment. The full profiles have wider thresholds because they deliberately apply higher concurrency.

By default the HTTP profiles call `/health/ready`. For a more representative read-only endpoint, set `K6_TEST_PATH`. Do not point automated load/stress tests at an endpoint that creates or deletes user data.

## Run locally

Install k6 (`brew install k6` on macOS), then:

```bash
BASE_URL=https://api.researchtrack.blipzo.xyz k6 run tests/Performance/smoke.js
```

Example with a different safe endpoint and threshold:

```bash
BASE_URL=https://api.researchtrack.blipzo.xyz \
K6_TEST_PATH=/health/ready \
K6_LOAD_P95_MS=750 \
k6 run tests/Performance/load.js
```

The GitHub webhook spike is opt-in because it reaches a real ingress endpoint. It uses one delivery ID per workflow run, so after the first accepted delivery the remaining requests exercise the duplicate-delivery path instead of creating thousands of database rows. GitHub's `ping` event is ignored by the application worker after ingestion.

## CI/CD placement

- Pull-request CI: .NET build/tests and infrastructure/static validation only.
- `Performance Smoke`: runs after a successful Production deployment, or manually.
- `Performance Full`: manual by default; an optional scheduled run can be enabled with the `ENABLE_NIGHTLY_PERFORMANCE` GitHub Environment variable.

The Azure workflows capture the VM/Container App state, power up anything required for the test, wait for the public `/health/ready` endpoint, run k6, and restore the exact captured state in an `always()` cleanup step.
