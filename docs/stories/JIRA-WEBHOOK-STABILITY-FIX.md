# Jira webhook stability hardening

This branch keeps the stateless Jira token-encryption fix and hardens the webhook-to-sync path that can differ between long-lived Test state and a newer Production environment.

## What changed

- An authenticated webhook is no longer assumed to be successfully routed just because the endpoint returned HTTP 200.
- All `matchedWebhookIds` are resolved instead of stopping at the first ResearchTrack connection.
- If the same Jira project is linked to multiple ResearchTrack projects, all matching connections in the same Jira cloud are queued for synchronization.
- Stale Atlassian webhook IDs can fall back to Jira project identity (`project.id`, `project.key`, or the issue-key prefix) when that can be done safely.
- Cross-cloud fallback is rejected when it would be ambiguous.
- Unmatched webhook deliveries are persisted as `IGNORED` with an explicit reason and a warning log containing the project identity and matched webhook IDs.
- `LastWebhookAt` is updated only for connections that were actually matched.
- Webhook deliveries are stored per ResearchTrack project when one Atlassian event maps to multiple connections.
- A retried webhook whose event was persisted before scheduling failed can requeue itself while it remains `RECEIVED`.
- A previously `IGNORED` single-target delivery can be promoted to `RECEIVED` if an Atlassian retry arrives after the connection becomes matchable.
- The sync scheduler refuses new work for `INVALID_AUTH` connections.
- The worker suppresses pending work for disconnected or `INVALID_AUTH` connections.
- Permanent Jira authorization failures mark the connection `INVALID_AUTH`, set the webhook state to `REAUTH_REQUIRED`, and cancel other pending jobs for that project.

## Expected webhook flow

`Atlassian -> authenticated webhook -> resolve connection(s) -> persist RECEIVED event(s) -> queue durable WEBHOOK sync -> full Jira snapshot sync -> mark covered webhook events PROCESSED`

A `200` webhook response by itself only means the request was authenticated/accepted. Operational verification should also confirm:

- `LastWebhookAt` changes for the intended connection.
- `jira_webhook_events` has the intended `ResearchProjectId` and status `RECEIVED`/`PROCESSED` rather than `IGNORED`.
- a `WEBHOOK` job appears in `jira_sync_jobs` and completes.
- `SyncRevision` increments and `LastSyncedAt` advances without manually calling `/jira/refresh`.

## Test reset note

Clearing the ResearchTrack Test database removes local stale connection/webhook state, but it does not delete dynamic webhook registrations held remotely by Atlassian. The routing fallback in this patch makes stale remote registrations less disruptive, but old Atlassian Test webhook registrations should still be removed when possible to avoid exhausting Atlassian webhook-registration limits.
