# JiraService webhook final survivor-hardening pass

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

Run only the final weak scope with:

```bash
bash ./scripts/mutation-baseline.sh jira-webhook-final
```

The normal Jira unit/mock suite and dedicated mutation harness are verified first.


## Robust JSON type handling fix

The final Jira webhook pass now treats heterogeneous `matchedWebhookIds` safely: only JSON numeric values that fit in `Int64` are accepted, while string/null/object values are ignored instead of throwing. JWT `exp`, when present, must be a JSON number before NumericDate parsing; malformed non-numeric claims are rejected cleanly rather than surfacing `InvalidOperationException`.
