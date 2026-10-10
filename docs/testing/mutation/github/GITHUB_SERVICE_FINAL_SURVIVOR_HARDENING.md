# GitHubService final survivor hardening

This pass targets the GitHubService mutation subscopes that still had meaningful survivor density after the project-wide baseline. The already-strong repository URL parser and webhook signature verifier are intentionally left unchanged. Reconciliation is also not used as the main score-improvement target because Stryker safe-mode compile-error removal leaves only a very small testable mutant denominator in that method.

The final hardening focuses on:

- access-request/request-result token purpose separation, deterministic recreation, request-id and nonce binding, and stable token hashing;
- installation-state exact-expiry boundaries, flow/user context binding, positive installation IDs, local return-path validation, and required project/user identifiers;
- repository-link validation, primary-selection rules, display-name boundaries, missing-link/source handling, enablement queue behavior, manual-sync failure recovery, and project/link ownership checks;
- webhook-event behavior for tag pushes, disabled/unavailable links, installation action validation, unmanaged installations, repository-scope additions, and inventory refresh routing.

Run the focused final pass with:

```bash
bash ./scripts/mutation-baseline.sh github-final
```

The full GitHub mutation portfolio remains available with `bash ./scripts/mutation-baseline.sh github`.
