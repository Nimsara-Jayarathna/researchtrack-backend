# Jira webhook + reconciliation synchronization

ResearchTrack keeps the local Jira mirror authoritative for the UI. Jira webhooks are a fast invalidation signal; periodic reconciliation is the correctness fallback.

## Runtime flow

1. Linking a Jira project performs the existing full snapshot sync and then registers an OAuth 2.0 dynamic webhook for issue create/update/delete events filtered to the linked Jira project.
2. `POST /api/v1/jira/webhooks` validates Atlassian's OAuth webhook bearer JWT using the configured Atlassian client secret, deduplicates `X-Atlassian-Webhook-Identifier`, persists the delivery, and coalesces a full-project sync job.
3. `JiraSyncWorker` processes persistent jobs with retry/backoff. It also schedules reconciliation for stale connected projects and refreshes webhook registrations before expiry.
4. `JiraSyncService` remains the single snapshot publisher: all remote reads finish before destructive reconciliation starts, and the local snapshot is committed transactionally.
5. Disconnect performs best-effort remote webhook deletion and removes local issues, sprints, memberships, webhook events, sync jobs, and the connection.

## Required configuration

- Add `manage:jira-webhook` to the Atlassian developer-console OAuth scopes and to `Jira__Scope`.
- Set `Jira__WebhookUrl` to the public HTTPS Jira-service callback, for example `https://api.example.com/api/v1/jira/webhooks` when the gateway routes that path to JiraService.
- Existing Jira authorizations must reconnect/consent again after the new scope is added.
- Run the JiraService EF migration `20260921180000_AddJiraWebhookReconciliation` before deployment.

Dynamic OAuth webhooks expire after 30 days. The worker refreshes them when fewer than seven days remain. Sprint metadata is repaired by the periodic full reconciliation even when no issue webhook is emitted for a sprint-only change.
