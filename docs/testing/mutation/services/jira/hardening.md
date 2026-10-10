# JiraService mutation hardening

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

These records preserve successive hardening passes. Scores below are historical measurements that motivated each pass, not newly verified results. Use the shared runbook for current service commands and the baseline document for scoring and gate status.

## Routing and authentication hardening

### Baseline before this pass

- Weighted JiraService: **39.22%** in the project-wide baseline.
- Issue mapper and sync-state scopes were already comparatively strong.
- Jira webhook orchestration was the dominant weak area at **20.00%** (59 killed / 236 survived).

### Hardening focus

The final Jira pass strengthens mutation-resistant assertions for JWT shape/algorithm/signature/expiry validation, matched-webhook routing, project-key and project-id fallbacks, multi-cloud ambiguity, same-cloud expansion, `INVALID_AUTH` filtering, duplicate deliveries, deterministic and long delivery IDs, retry behavior, promotion of previously ignored events, connection-health updates, and the webhook coalescing boundary.

The same behavioral coverage is kept in the normal xUnit v3 suite and the dedicated xUnit v2 Stryker harness, with cancellation-token handling appropriate to each test framework.

## Webhook lifecycle follow-up

Baseline before this pass:

- Issue mapper: 79.22%
- Sync state transitions: 83.33%
- Issue query: 63.89%
- Sprint progress: 65.91%
- JiraWebhookService: 44.07% (130 killed / 165 survived)
- Weighted JiraService: 54.53% (253 killed / 211 survived)

The non-webhook scopes are frozen. This pass targets `JiraWebhookService`, including both webhook receipt/routing and webhook registration/removal because the Stryker scope mutates the complete service file.

Added assertions cover exact delivery-id boundaries, delivery-id trimming, issue identity propagation, duplicate/non-numeric matched webhook IDs, project-identity precedence, multi-target IGNORED behavior, JWT expiry shape validation, registration states for invalid auth / missing configuration / active registrations, refresh success and fallback replacement, new registration request content, degraded-state behavior after Atlassian failure, and exact delete behavior.

`ValidateBearer` now rejects a present `exp` claim when it is not a valid JWT NumericDate. A token without `exp` remains accepted, preserving the prior optional-expiry contract while removing malformed-expiry ambiguity.

### Robust JSON type handling fix

The final Jira webhook pass now treats heterogeneous `matchedWebhookIds` safely: only JSON numeric values that fit in `Int64` are accepted, while string/null/object values are ignored instead of throwing. JWT `exp`, when present, must be a JSON number before NumericDate parsing; malformed non-numeric claims are rejected cleanly rather than surfacing `InvalidOperationException`.

## Verification

Run `bash ./scripts/mutation-baseline.sh jira` from the repository root. This verifies both test suites and executes every finalized JiraService subscope. Follow the [runbook](../../runbook.md) for reporting and survivor review.
