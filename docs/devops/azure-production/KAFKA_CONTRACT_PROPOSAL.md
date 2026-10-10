# GitHub/Jira Kafka contracts — approved for local implementation

Prepared and explicitly approved by the owner in this session on 2026-10-10.
Approval covers the complete same-service routing proposal and local implementation/tests.
**It does not authorize production topic creation, enablement or deployment.**
Review the business routing below before implementation. The existing HTTP APIs,
authentication, GitHub delivery worker and Jira synchronization worker remain available
when Kafka is disabled or unavailable.

## Evidence and missing decision

GitHub authenticates a webhook, stores `GitHubWebhookDelivery`, and processes it through
`GitHubWebhookDeliveryWorker` → `GitHubWebhookEventProcessor` → repository synchronization.
Jira authenticates/deduplicates a webhook, stores `JiraWebhookEvent`, then schedules
`JiraSyncJob` → `JiraSyncWorker` → transactional Jira snapshot synchronization.
There is no approved Kafka payload or receiving business service in source. Accessible
Jira search found the platform epic [SCRUM-198](https://researchtrack.atlassian.net/browse/SCRUM-198),
which mentions Kafka infrastructure but specifies no event contract. Rovo search reported
partial-source warnings; this is not proof that no agreement exists elsewhere.

## Proposed routing to existing processing

To avoid inventing a new business consumer, propose **same-service durable webhook
routing**. Kafka carries a reference to an already accepted database inbox record.
GitHubService consumes GitHub references and JiraService consumes Jira references. The
consumer validates identity against its own inbox and hands work to existing durable
processing. Database workers retain recovery/polling; no raw webhook is copied to Kafka.

This choice needs approval: it adds a transport route to existing synchronization,
rather than delivering completed-sync notifications to another microservice. If Project,
Submission or another service is intended to receive events, supply its precise processing
outcome instead; that requires a different contract and handler.

| Decision | GitHub proposal | Jira proposal |
|---|---|---|
| Event name | `github.webhook.ready` | `jira.webhook.ready` |
| Business purpose | Route an accepted delivery to existing repository processing | Route an accepted, matched delivery to existing project synchronization |
| Producer and data owner | GitHubService / GitHub team | JiraService / Jira team |
| Consumer | GitHubService, existing durable delivery processing | JiraService, existing persistent sync scheduler/worker |
| Topic | `researchtrack.github.events.v1` | `researchtrack.jira.events.v1` |
| Schema/version | Strict JSON object, string `contractVersion: "1"` | Strict JSON object, string `contractVersion: "1"` |
| Message key | `installation:<installationId>`; publish only mapped installation deliveries | Research project GUID in canonical `D` format |
| Consumer group | `researchtrack-github-webhook-v1` | `researchtrack-jira-webhook-v1` |
| Ordering | One partition initially; installation-key order, while DB workers retain current concurrency semantics | One partition initially; project-key order, while scheduler retains coalescing semantics |
| Partitions / replication / retention | 1 / 1 / 72 hours | 1 / 1 / 72 hours |

Required envelope fields: `eventId` (stable inbox GUID), `eventName` (above),
`contractVersion` (`"1"`), `occurredAtUtc` (UTC timestamp), `correlationId` (stable inbox GUID),
and `data` (object). No optional arbitrary metadata or raw payload field.

GitHub `data`: `deliveryRecordId` (GUID), `deliveryId` (original provider delivery ID,
bounded to 128 characters), `installationId` (positive integer), `eventType` (one of the
existing processor's supported events). Repository identity, if present in the inbox,
can be an optional positive `repositoryId`. Ping/unsupported/unmapped events are excluded.
Existing lifecycle events must still use their current processor; Kafka does not change
installation authorization, repository linking or cleanup decisions.

Jira `data`: `webhookRecordId` (GUID), `researchProjectId` (GUID), `cloudId` (bounded provider
cloud identity), `eventType` (issue created/updated/deleted). Unmatched/ignored deliveries
are excluded. Consumers verify the currently active connection, not just IDs in a message.

## Proposed reliability and security

- Persist the inbox record and its immutable outbox event in the **same database transaction**.
  An accepted webhook must remain recoverable if Kafka is offline. Duplicate provider
  deliveries must reuse the original outbox/event identity, never create another event.
- An outbox publisher leases bounded batches, publishes with idempotence and `acks=all`,
  and records success only after acknowledgement. Per-send timeout 30 seconds; durable
  retries after 30 seconds, 2 minutes, 10 minutes, 30 minutes and 1 hour; after 10 attempts
  retain the failed row and require operator replay. No deletion on exhaustion.
- Delivery is **at least once**, including an acknowledgement/DB-update crash window.
  Producer idempotence alone cannot prevent outbox replay duplicates across restarts.
- Consumer validates the envelope, version, key and inbox ownership. Deduplicate by
  `eventId` using a durable receipt and an atomic handoff to durable processing. Existing
  DB leases/status transitions must prevent a Kafka delivery and polling fallback from
  executing the same logical delivery concurrently. Already processed records are no-ops.
- Commit offsets only after that durable handoff/receipt succeeds. The business worker
  remains responsible for completing synchronization and existing business retries.
  Kafka receipt acknowledgement is **not** evidence that remote synchronization completed.
- Retry a transient handoff failure three times with bounded backoff. On an invalid
  schema/version/key or exhausted failure, stop consumption without committing that
  record; alert for review/replay. Do not silently skip or create an unapproved DLQ topic.
- Shutdown stops intake, cancels work safely, and closes the consumer. Restart resumes
  from committed offsets; pending DB/outbox work survives. No startup topic creation.
- Preserve SSL, public CA trust and endpoint identity verification. No GitHub/Jira tokens,
  authorization headers, signing secrets, private keys or full provider payloads enter Kafka.
  Keep identifiers/payloads out of error logs; evidence uses synthetic correlation GUIDs.
- RF1 has no broker redundancy; acks/idempotence do not make this deployment fault tolerant.

## Required processing evidence after approval

For each service, a synthetic signed/authenticated webhook must create an inbox/outbox,
receive a broker acknowledgement, reach its real consumer, create a durable receipt/handoff,
and produce the existing synchronization outcome verified from database/API state.
Repeat delivery and exercise broker outage, publication timeout, process restart before
acknowledgement bookkeeping, consumer failure/rebalance, invalid schema/version, and
restart with pending work. Use stub provider HTTP APIs and isolated databases/Kafka for
local tests; a test-only receipt store is not production business-processing evidence.

## Approval requested

Confirm the complete same-service routing proposal above, including event selection,
payloads, keys, groups, outbox boundary, offset policy and failure handling; or provide
the intended different consumer and processing outcome. Approval permits implementation
and tests locally. It does **not** authorize production enablement, topic creation,
secret updates, deployment, restart, commit or push.
