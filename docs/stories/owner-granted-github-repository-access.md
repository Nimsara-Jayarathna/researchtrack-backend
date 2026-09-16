# Owner-granted GitHub App access request

## Purpose

ResearchTrack has one GitHub integration mechanism: the ResearchTrack GitHub App. **Request Access is not a second GitHub integration.** It is an orchestration layer that lets a Supervisor ask a GitHub user or organization owner to complete the same GitHub App installation flow on the project's behalf.

The direct and requested flows converge on the same installation verification, access-source persistence, repository discovery, repository-link verification, and synchronization pipeline.

## Business flows

```text
DIRECT
Supervisor -> Connect GitHub -> GitHub App install -> callback
           -> access source -> choose repository -> link -> sync

REQUESTED
Supervisor -> create request for GitHub owner/org -> secure link
           -> external owner opens link -> same GitHub App install -> same callback
           -> requested access source -> Supervisor chooses repositories -> link -> sync
```

Request completion deliberately does **not** auto-link a repository. An installation can expose multiple repositories, so the Supervisor chooses the repository or repositories after access is granted. Each selection is re-verified with a short-lived GitHub App installation token immediately before the project link is persisted.

## Request lifecycle

Requests use `PENDING`, `COMPLETED`, `FAILED`, `EXPIRED`, and `REVOKED`. Only `PENDING` requests can start authorization or be revoked. Expiry is enforced by the backend. The configured request lifetime is `GitHub__AccessRequestExpiryHours` (1–168 hours; the committed development example uses 24). GitHub callback state has its own shorter `GitHub__StateExpiryMinutes` lifetime.

Only one pending request is allowed per ResearchTrack project at a time. A Supervisor can revoke a pending request and create a replacement.

## Security boundary

- The request records the ResearchTrack project, requesting user, project title, and expected GitHub user/organization login.
- The shareable request token is opaque, HMAC-protected and hashed in storage; raw request tokens are not stored.
- The external recipient does not need a ResearchTrack session merely to complete GitHub App authorization.
- The callback state is server-side, time-bounded and bound to the request ID, project ID, initiating ResearchTrack user, flow type, return path, and installation ID.
- After GitHub returns the installation, ResearchTrack verifies the installation through the GitHub App API and verifies that the installation owner login matches the owner/org named in the request.
- Project IDs, request IDs, repository IDs, and installation IDs supplied by a browser are never accepted as authorization by themselves.
- GitHub App installation tokens remain backend-only and short-lived.
- The external completion result uses a separate one-time result token. Consuming that token does not clear the Supervisor's pending “access granted” notification.

## API responsibilities

| Operation | Authorization | Behavior |
| --- | --- | --- |
| `POST /api/github/access-source/request` | Supervisor | Create a pending owner/org-bound request. |
| `GET /api/github/access-source/requests?projectId=...` | Supervisor | List recent request states and the pending share link. |
| `DELETE /api/github/access-source/requests/{id}?projectId=...` | Supervisor | Revoke a pending request. |
| `GET /api/github/access-requests/validate?token=...` | Opaque request token | Return safe project/owner/status/expiry context. |
| `POST /api/github/access-requests/continue?token=...` | Opaque request token | Start the shared GitHub App installation flow. |
| `GET /api/github/access-source/install/callback` | GitHub + bound state | Verify installation, create/update the normal GitHub App access source, and complete/fail the request. |
| `GET /api/github/access-updated/summary?token=...` | One-time result token | Return safe completion context to the external owner. |
| `POST /api/github/access-updated/acknowledge?token=...` | One-time result token | Consume the external result token only. |
| Supervisor access-updated summary/acknowledge routes | Supervisor | Surface and clear newly granted access for repository selection. |

## Repository lifecycle

Repository management is separate from access-source authorization:

- **Enable** keeps or reactivates the project link and queues synchronization.
- **Disable** keeps the link and synchronized history but pauses new synchronization.
- **Unlink** deactivates only that project-repository relationship.
- **Disconnect source** deactivates the GitHub App access source and all active project links that belong to it. It does not uninstall the app from GitHub.
- If the primary repository is disabled/unlinked, ResearchTrack deterministically promotes the oldest remaining enabled link.
- `IN_PROGRESS` synchronization blocks disable, unlink, and source disconnect in the backend. Manual refresh also rejects a repository whose sync is already `PENDING` or `IN_PROGRESS`.

The frontend mirrors these restrictions for usability, but the backend is authoritative.

## Legacy access model cleanup

The current integration model is GitHub App-backed only. Historical EF migration files keep their original names because deployed databases may already have recorded those migration IDs; current runtime code and the current EF model contain only GitHub App-backed access paths.

## Verification expectations

Automated coverage should include request creation/expiry/revocation, owner mismatch, callback failure/success, requested-flow reuse of the shared installation pipeline, repository verification before link, enable/disable/unlink, primary promotion, synchronization conflicts, source disconnect, duplicate callback/source races, and prevention of legacy access-path regressions.
