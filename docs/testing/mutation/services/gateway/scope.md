# Gateway mutation scope

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

Gateway mutation testing covers two security-sensitive policy subscopes:

- `performance-rate-limit`: performance-test rate-limit bypass policy.
- `trusted-proxy`: trusted proxy forwarding configuration.

The dedicated harness is [ResearchTrack.Gateway.MutationTests](../../../../../tests/ResearchTrack.Gateway.MutationTests/); the normal xUnit v3 suite remains authoritative for regular CI and coverage. Each subscope uses its own configuration under the harness's `configs/` directory.

Run `bash ./scripts/mutation-baseline.sh gateway` from the repository root to verify both suites and execute both subscopes. The recorded hardened baseline is 78.08%, with an intended aggregate floor of 72%; see the baseline document for the current aggregate-policy limitation. No separate Gateway survivor-hardening narrative existed in the original documentation set.
