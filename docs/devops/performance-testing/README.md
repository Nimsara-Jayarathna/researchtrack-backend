# Performance testing strategy

ResearchTrack separates fast correctness checks from deployed-system performance checks.

```text
Pull request
    |
    +--> Backend CI
          restore -> build -> unit/integration-safe tests -> deployment validation

main deployment succeeds
    |
    +--> Performance Smoke
          capture Azure state
          -> start VM + Container Apps if required
          -> wait for /health/ready
          -> k6 smoke (5 VUs / 15 s)
          -> restore Azure state

manual / optional scheduled run
    |
    +--> Performance Full
          capture/start/wait
          -> k6 load
          -> k6 stress (manual/full profile)
          -> optional signed GitHub webhook spike
          -> restore Azure state
```

## Why the stress test is not a blocking PR check

A stress test intentionally drives infrastructure toward saturation. It is slower, noisier, more expensive, and more dependent on the current cloud environment than a correctness test. More importantly, running a PR k6 test against Production would test the already-deployed `main` revision rather than the PR's code.

Therefore ResearchTrack uses:

- **PR CI** for deterministic code-level quality gates.
- **Post-deployment smoke** as a small deployed-environment performance gate.
- **Full load/stress** as an isolated manual/optional scheduled workflow for deeper analysis.

A concise viva explanation is:

> We do not execute the stress profile on every commit because stress testing deliberately pushes shared infrastructure toward saturation and is environment-sensitive. Pull requests use deterministic correctness gates, the deployed build receives a lightweight k6 smoke test, and deeper load/stress profiles run separately on demand or on a controlled schedule.

## Threshold policy

Thresholds should detect meaningful regressions rather than normal cloud jitter. If repeated baseline smoke p95 values are around 180–215 ms, setting a gate at 220 ms would be too fragile. The committed smoke default is therefore `p95 < 500 ms`; it should be tightened only after enough measurements exist from the real deployment.

Committed defaults:

| Profile | HTTP failure rate | p95 response time |
|---|---:|---:|
| Smoke | `< 1%` | `< 500 ms` |
| Load | `< 2%` | `< 750 ms` |
| Stress | `< 5%` | `< 1200 ms` |
| Webhook spike | `< 3%` | `< 1000 ms` |

These are starting quality gates, not claims about final SLOs.

## Cost-aware Azure runtime lifecycle

Production uses seven Azure Container Apps plus an infrastructure VM that hosts MySQL, Kafka, Nginx, Prometheus, and Grafana. The VM is deallocated when the project does not need to be live.

The same commands are exposed through the manual GitHub Actions workflow **Azure Runtime Power - Production**, with a confirmation requirement before stopping Production.

`deploy/azure/scripts/runtime-power.sh` supports:

```bash
./deploy/azure/scripts/runtime-power.sh status
./deploy/azure/scripts/runtime-power.sh capture /tmp/rt-state.json
./deploy/azure/scripts/runtime-power.sh start
./deploy/azure/scripts/runtime-power.sh stop
./deploy/azure/scripts/runtime-power.sh restore /tmp/rt-state.json
```

The performance workflows use `capture -> start -> test -> restore`. This is safer than an unconditional stop at the end: if the environment was already running for a demo or release, the workflow leaves it running; if the workflow had to start a normally-off environment, it returns it to the stopped/deallocated state.
