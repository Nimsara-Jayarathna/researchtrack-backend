# Service configuration reference

Start with [local setup](local-setup.md) and [configuration loading rules](secrets.md).
Use the complete `config/env/<service>/.env.example` shape; the key groups below
are a navigation aid, not a replacement for the validated contract.

## Gateway and shared authentication

Gateway needs `FRONTEND_ORIGIN` plus `AUTH_SERVICE_URL`, `PROJECT_SERVICE_URL`,
`GITHUB_SERVICE_URL`, `JIRA_SERVICE_URL`, `MEETING_SERVICE_URL` and
`SUBMISSION_SERVICE_URL`. Locally these normally point to ports 5101–5106.
Friendly variables map to ASP.NET CORS/YARP settings at startup.

All protected business services load shared `Jwt__Issuer`, `Jwt__Audience` and
`Jwt__SigningKey`. Each business service separately configures `Database__*` or
its explicit connection string. `OpenApi__Enabled` controls documentation outside
Development; see [Swagger setup](../api/openapi.md).

## Auth

Configure `Registration__*` policy/OTP/session limits, `PasswordPolicy__*`,
`PasswordHashing__*`, `Brevo__*` transactional email settings, `PasswordReset__*`,
`Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays` and `Cookie__Secure`.
Use secure cookies on HTTPS deployments. See [registration](../stories/registration.md)
and [session/authentication guidance](us-102-secure-login-rbac.md).

## Project and Meeting

Configure each service's database and shared JWT settings, and service dependency
URLs required by its template. Project owns membership/resource access rules;
Meeting owns meeting records and approval workflows. Optional `Kafka__*` entries
are not evidence that every service publishes events; use the approved contracts.

## GitHub

Configure GitHub App identity (`GitHub__AppId`, `GitHub__AppSlug`,
`GitHub__ClientId`, `GitHub__ClientSecret`), private key material
(`GitHub__PrivateKeyBase64` or a mounted `GitHub__PrivateKeyPath`), callback and
frontend return URLs, state/access-request expiry, repository limits, webhook
settings and reconciliation cadence.

`GitHub__RepositoryLinks__MaxLinkedRepositories` and
`GitHub__RepositoryLinks__MaxEnabledRepositories` are required. Startup rejects
missing/invalid values or an enabled limit greater than the linked limit.

GitHub synchronization is webhook-driven for near-real-time updates, with manual sync available to supervisors and a lightweight scheduled reconciliation safety net. `GitHub__SyncIntervalMinutes` controls the reconciliation cadence (1-1440 minutes, default/canonical value 15). Each reconciliation cycle checks only the current default branch and its HEAD SHA; unchanged repositories are not fully synchronized. If the remote default branch or HEAD SHA differs from the last successfully synchronized values, the existing synchronization queue is used with trigger `RECONCILIATION`.

The webhook receiver is `POST /api/github/webhooks` (also available under `/api/v1/github/webhooks`). It is intentionally anonymous at the ResearchTrack authentication layer because GitHub authenticates deliveries using `X-Hub-Signature-256`. ResearchTrack validates HMAC-SHA256 over the exact raw request body using `GitHub__WebhookSecret`, deduplicates `X-GitHub-Delivery`, persists the delivery before returning `202 Accepted`, and processes it asynchronously from the durable webhook inbox. Failed transient deliveries use bounded exponential retry and stale `PROCESSING` leases are recovered after service restart. `GitHub__Webhook__PollIntervalSeconds` defaults to 5 seconds and provides a bounded durable-inbox poll so webhook work persisted by another service instance is still discovered even though the in-process wake signal is local to one instance.

Configure the GitHub App webhook URL to the public API route and subscribe to `push`, `pull_request`, `pull_request_review`, `pull_request_review_comment`, and `repository`. GitHub App lifecycle deliveries `installation` and `installation_repositories` are also handled. Push events synchronize only the linked default branch. Repository removal, installation suspension, and installation deletion preserve ResearchTrack history while blocking synchronization until GitHub access is restored. Unknown webhook events are acknowledged and marked ignored instead of failing the receiver.

For SCRUM-15 GitHub App registration, least-privilege permissions, callback configuration, and manual QA, see [GitHub App setup and manual QA](../stories/SCRUM-15-connect-authorized-github-app.md).

For the owner-granted GitHub App access-request contract—including pending/completed/failed/expired/revoked states, time-bounded shareable links, owner binding, shared GitHub App callback handling, and supervisor-side repository selection—see [Owner-granted GitHub repository access](../stories/owner-granted-github-repository-access.md). A request grants no repository link by itself; every linked repository is still verified through the GitHub App installation token before persistence.

## Jira

Configure `Jira__ClientId`, `Jira__ClientSecret`, `Jira__TokenEncryptionKey`,
`Jira__RedirectUri`, scopes/audience/provider URLs, OAuth state and selection TTLs,
Atlassian/project-service timeouts, and `Services__Project__BaseUrl` as specified
by the canonical template. Supply public HTTPS callback URLs for deployments.
Jira synchronization includes authenticated webhooks and durable scheduling with
reconciliation; see [Jira synchronization](../architecture/JIRA-WEBHOOK-RECONCILIATION.md)
and [webhook hardening](../stories/JIRA-WEBHOOK-STABILITY-FIX.md).

## Submission and object storage

S3-compatible storage is implemented. Configure the actual bucket, credentials,
region/endpoint/path-style behavior and upload/presigned-URL limits through
`Storage__*`, plus `Submission__*` file-type, upload-session, cleanup and dependency
settings. Do not treat storage as an unimplemented placeholder. Refer to the
[storage foundation](../submission/submission-s3-foundation.md) and
[review workflow](../submission/ResearchTrack_Submission_Review_Workflow.md).

## Optional Kafka transport

GitHub and Jira implement approved same-service webhook-reference transports with
transactional outboxes and durable receipts. Keep Kafka disabled until broker,
TLS trust, topic/version and consumer-group requirements are satisfied. Existing
durable workers remain responsible for provider synchronization. Consult
[Kafka topics and event flows](../devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md)
for the complete contract and operational verification; do not infer enablement
from a blank or example bootstrap-server value.
