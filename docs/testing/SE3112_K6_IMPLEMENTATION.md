# ResearchTrack SE3112 — k6 performance implementation (Nimsara)

The complete performance suite builds on the existing Azure runtime acquisition/restoration automation, existing Gateway and ResearchTrack service routes, existing cookie-based login, and the **same SSH repository secrets / VPS user used for Test deployment**. No duplicate VPS secrets or static performance-runner IP configuration is required.

## What the final automation does

- GitHub `workflow_dispatch` starts a protected performance session for `smoke`, `load`, `stress`, `spike`, `write`, `lifecycle`, or `webhook`.
- Existing repository secrets `SSH_HOST`, `SSH_PORT`, `SSH_PRIVATE_KEY`, `SSH_USER` connect to the existing Test VPS; when the optional Gateway allowance is enabled, the VPS's **outbound** IPv4 is detected from two HTTPS sources and validated.
- The sole on/off switch is the repository-level Actions **variable** `K6_RATE_LIMIT_BYPASS_ENABLED` (default `false`). No static IP is stored anywhere in GitHub variables or source code.
- Gateway source already contains `PerformanceRateLimitPolicy` with **IP + secret header** matching for the general 120/min bucket only. The workflow configures this **on the deployed Azure Gateway** by copying the original revision to a temporary variant, using a unique Container App secret reference for `K6_PERFORMANCE_TOKEN`.
- Original revision template and traffic are recovered by creating a rollback revision from the captured original. The temporary secret is removed, and results/recovery artifacts are published regardless of k6 gate success.
- Existing `runtime-session.sh` restores **original Azure power state**; the new gateway-session wrapper restores Gateway **configuration first**, then the runtime-session power restoration runs. The workflow has independent `always()` restoration safeguards.
- Existing backend/frontend testing remains intact. Backend CI additionally runs shell/JavaScript validation and mock regression tests covering VPS SSH execution, IP discovery, Gateway revision restoration, remote failures and temporary credential cleanup.

## Prerequisites & setup

See **[`tests/performance/README.md`](../../tests/performance/README.md)** for exact GitHub repository secrets, the one new variable, GitHub production Environment values, pre-existing Gateway code deployment requirement, and execution steps. Never commit credentials or paste them into scripts.

## Verification boundary

Offline source checks and mocked Azure/VPS tests can be run in the supplied source tree. Actual `.NET` CI requires the .NET SDK. **Live deployment, Azure Container App CLI behavior, the actual forwarded VPS IP, real k6 execution and final pass/fail results must still be verified in the configured GitHub/Azure/VPS environment**. No production performance results are invented or claimed.
