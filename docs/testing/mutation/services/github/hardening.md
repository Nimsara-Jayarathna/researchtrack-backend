# GitHubService mutation hardening

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

These records preserve successive hardening passes. Scores below are historical measurements that motivated each pass, not newly verified results. Use the shared runbook for current service commands and the baseline document for scoring and gate status.

## Token, installation, linking, and webhook hardening

This pass targets the GitHubService mutation subscopes that still had meaningful survivor density after the project-wide baseline. The already-strong repository URL parser and webhook signature verifier are intentionally left unchanged. Reconciliation is also not used as the main score-improvement target because Stryker safe-mode compile-error removal leaves only a very small testable mutant denominator in that method.

The final hardening focuses on:

- access-request/request-result token purpose separation, deterministic recreation, request-id and nonce binding, and stable token hashing;
- installation-state exact-expiry boundaries, flow/user context binding, positive installation IDs, local return-path validation, and required project/user identifiers;
- repository-link validation, primary-selection rules, display-name boundaries, missing-link/source handling, enablement queue behavior, manual-sync failure recovery, and project/link ownership checks;
- webhook-event behavior for tag pushes, disabled/unavailable links, installation action validation, unmanaged installations, repository-scope additions, and inventory refresh routing.

## Reconciliation scope refinement

### Why this final refinement exists

The hardened GitHub baseline is already strong in the externally exposed parsing and security rules:

- Repository URL: 87.23% (41 killed / 6 survived)
- Webhook signature: 78.57% (11 / 3)
- Access request token: 77.78% (14 / 4)
- Installation state: 57.39% (66 / 49)
- Repository link: 59.46% (66 / 45)
- Webhook event processor: 65.88% (112 / 58)

The original reconciliation scope reported 37.50% (3 / 5), but that percentage was based on only eight testable mutants while 55 generated mutants were compile errors. That made the percentage disproportionately sensitive to Stryker/C# async-EF instrumentation rather than the actual reconciliation business decision.

### Final scope refinement

`GitHubReconciliationService` still owns the real orchestration and is covered by its service tests. The mutation target is now the extracted deterministic `GitHubReconciliationPolicy.HasRemoteChange` rule used by the service.

The policy intentionally preserves the production semantics:

- default-branch comparison is ordinal/case-sensitive;
- commit SHA comparison is ordinal-ignore-case;
- either a branch change or a head change requires synchronization;
- missing local snapshot information is treated as changed.

This is not a score-only exclusion: the production service calls the policy directly, and the dedicated mutation tests verify every independent condition and boundary while avoiding compiler-noise from generated async/EF mutants.

## Verification

Run `bash ./scripts/mutation-baseline.sh github` from the repository root. This verifies both test suites and executes every finalized GitHubService subscope. Follow the [runbook](../../runbook.md) for reporting and survivor review.
