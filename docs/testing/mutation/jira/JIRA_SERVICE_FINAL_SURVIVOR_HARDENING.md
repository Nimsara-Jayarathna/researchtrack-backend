# JiraService Final Survivor Hardening

## Baseline before this pass

- Weighted JiraService: **39.22%** in the project-wide baseline.
- Issue mapper and sync-state scopes were already comparatively strong.
- Jira webhook orchestration was the dominant weak area at **20.00%** (59 killed / 236 survived).

## Hardening focus

The final Jira pass strengthens mutation-resistant assertions for JWT shape/algorithm/signature/expiry validation, matched-webhook routing, project-key and project-id fallbacks, multi-cloud ambiguity, same-cloud expansion, `INVALID_AUTH` filtering, duplicate deliveries, deterministic and long delivery IDs, retry behavior, promotion of previously ignored events, connection-health updates, and the webhook coalescing boundary.

The same behavioral coverage is kept in the normal xUnit v3 suite and the dedicated xUnit v2 Stryker harness, with cancellation-token handling appropriate to each test framework.

## Verification

```bash
bash ./scripts/mutation-baseline.sh jira
```

The command verifies the normal unit/mock suite, verifies the dedicated mutation harness, runs the issue-mapper, sync-state, issue-query, sprint-progress, and webhook Stryker scopes, and refreshes the mutation summary.

Do not average subscope percentages. The service score is `killed / (killed + survived)`. Ignored and compile-error mutants remain visible but are excluded from the surviving-business-mutant denominator.
