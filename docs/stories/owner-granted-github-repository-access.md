# Owner-granted GitHub repository access

## Story contract

An authorized ResearchTrack project member who cannot manage GitHub App access may create a pending request for one exact GitHub repository. A repository owner or administrator follows a time-bounded authorization URL. ResearchTrack recovers the server-side request context, verifies that the resulting GitHub App installation can access that exact repository, and only then creates the active project-to-repository connection and requests initial synchronization.

The request and active connection are separate records. Creating a request, receiving an `installation_id`, or accepting a private repository URL must not create an active connection.

## Security boundary

- GitHub credentials remain between the repository owner, GitHub, and backend-only GitHub App clients.
- The requester and browser never receive or store the owner's GitHub password, PAT, GitHub user token, installation token, App private key, or OAuth client secret.
- Shareable request tokens are high-entropy, opaque, and time-bounded bearer values. Persist only a cryptographic hash of each token. Terminal or expired requests cannot be continued; GitHub installation state itself is strictly one-time.
- Log the request identifier and safe lifecycle events, not raw request/state tokens, authorization codes, or credentials.
- A GitHub callback is untrusted until its state, expiry, request lifecycle, installation ownership, and exact repository access are verified server-side.

The existing `GitHub__StateExpiryMinutes` rule is 1–30 minutes, with 10 minutes in the committed example. Owner-granted request/state values should use that configured short lifetime unless a separate implementation rule is introduced and documented.

## Persisted request context

The pending record must be sufficient to complete the flow without trusting browser-supplied project or repository data:

- request ID;
- Research Project ID;
- initiating ResearchTrack user ID;
- normalized requested repository owner and name (canonical full name);
- optionally the stable GitHub repository ID when it was already obtained from a trusted GitHub response;
- token/state hash;
- flow type `INSTALLATION_REQUESTED`;
- status (`PENDING`, `COMPLETED`, `FAILED`, or `EXPIRED`);
- creation and expiry timestamps;
- callback-bound installation ID once known;
- completion/consumption timestamp;
- safe failure code where applicable.

Never persist GitHub user or installation access tokens in this request.

## Lifecycle

```text
                 owner/admin continues
PENDING ------------------------------------> GitHub authorization
   |                                                   |
   | expires                                           | callback denied/error
   v                                                   v
EXPIRED                                              FAILED
                                                       ^
                                                       |
                         wrong repository / invalid installation
                                                       |
GitHub authorization -> validate request -> verify exact repository
                                               |
                                               | verified + link committed
                                               v
                                           COMPLETED
```

`COMPLETED`, `FAILED`, and `EXPIRED` are terminal for the token. Expiry may be materialized by cleanup or derived atomically during validation, but externally it must be reported as `EXPIRED`.

## End-to-end processing

1. Authenticate the requester and authorize membership/management for the Research Project using the existing Project Service authorization boundary.
2. Parse and normalize the target `github.com/{owner}/{repository}` identity. Do not probe a private URL anonymously and call that authorization.
3. Confirm that adding the requested repository is allowed under the existing repository-link rules. The current runtime supports multiple repository links per project subject to configured `MaxLinkedRepositories` / `MaxEnabledRepositories`; reject an exact repository already actively linked.
4. Create `PENDING` request state, generate a high-entropy one-time token, persist only its hash, and return the shareable frontend URL plus expiry.
5. When the owner opens the URL, hash and look up the token, validate status/expiry, and return only safe project/repository context.
6. On continue, atomically re-check the pending request and generate a one-time GitHub App setup state bound server-side to that request. Reuse the current SCRUM-15 App installation security contract described below.
7. On callback, validate the ResearchTrack state before using GitHub's `installation_id`; recover the original project, requester, and requested repository from storage.
8. Verify the installation with the ResearchTrack GitHub App identity. GitHub enforces whether the person completing the App setup is permitted to install/configure the App; the current ResearchTrack runtime does not perform a second user-OAuth/PKCE proof.
9. List or retrieve installation repositories with a short-lived installation token. Match the requested canonical owner/name, then confirm the stable repository ID and fetch current canonical URL/default branch from GitHub. A repository with a different owner/name is a failed outcome, even if it belongs to the same installation.
10. In an idempotent transaction, re-check that the request is still pending and unexpired, enforce the existing configured project link/enabled limits plus exact-repository uniqueness, persist the installation-backed access source and exact repository metadata/link, mark the request `COMPLETED`, and record consumption.
11. After durable completion, invoke the shared initial synchronization boundary once for the created link. A transactional outbox or an equivalently reliable handoff is preferred so a process failure cannot leave a completed link permanently unsynchronized.

## Repository-limit reconciliation

The story wording about one valid connection is reconciled with the current implemented architecture rather than used to change global behavior. `GitHub:RepositoryLinks:MaxLinkedRepositories` and `MaxEnabledRepositories` are the authoritative runtime capacity controls (currently 5/5 in committed configuration). `project_repository_links.ActiveRepositoryKey` prevents the same GitHub repository from being actively linked twice to one project, while `PrimaryProjectKey` permits only one primary link. An owner-grant request targets exactly one repository, but successful SCRUM-17 completion may coexist with other distinct links while those existing limits allow it.

## Implemented API flow

Both unversioned frontend-contract routes and `/api/v1` aliases are exposed by the GitHub Service/Gateway. The SCRUM-17 routes are:

| Operation | Route | Authorization | Implemented result |
| --- | --- | --- | --- |
| Create owner-granted request | `POST /api/github/access-requests` | Authenticated `SupervisorOnly` plus Project Service `EnsureCanManageAsync` | `201`; request ID, project ID, normalized repository summary, `PENDING`, expiry, and shareable frontend request URL. |
| Get member-visible request status | `GET /api/github/access-requests/{requestId}` | Authenticated `SupervisorOnly`; Project Service authorization plus initiating-user check | Current lifecycle state, safe requested-repository fields, expiry, and allow-listed failure code. |
| Validate public owner link | `GET /api/github/access-requests/validate?token=...` | Anonymous bearer link | Safe requested-repository context, state, expiry, and allow-listed failure code. Malformed and unknown tokens share the same unavailable response. |
| Continue to GitHub | `POST /api/github/access-requests/continue?token=...` | Anonymous bearer link | Request ID, expiry, and backend-generated canonical GitHub App installation URL after an atomic pending/expiry check and server-side request/state binding. |
| GitHub App setup callback | `GET /api/github/access-source/install/callback` | Anonymous GitHub redirect carrying one-time ResearchTrack state | Validates persisted state/request context, verifies the App installation and exact requested repository, completes the owner request when safe, then redirects to the configured frontend result route. |

The create JSON body is `{ projectId, repositoryUrl }`. A project-only request is intentionally not supported because the request must be bound to one exact normalized `owner/name` before the shareable URL is issued. The callback does not accept browser-supplied project or repository context.

There is no separate SCRUM-17 public result API. The repository owner receives the callback result through `/github/access-updated`; the frontend renders requested-flow success without repository selection. If the original requester is signed in, the page may call the authenticated request-status endpoint by the callback-provided safe request ID to offer navigation back to the Research Project.


## Relevant configuration

SCRUM-17 reuses the existing GitHub App and repository-link configuration; no new credential setting was added. Relevant settings are:

- `GitHub__AppId` / `GitHub__AppSlug` — GitHub App identity used for installation verification and the canonical install URL.
- `GitHub__SetupCallbackUrl` — the registered callback URL; local HTTP is permitted only by the existing development validation rule.
- `GitHub__FrontendReturnOrigin` — trusted frontend origin used to create the owner share URL and callback return URL. It is not taken from request input.
- `GitHub__StateExpiryMinutes` — 1–30 minutes; the same short lifetime is used for the owner request and its installation state.
- `GitHub__ClientId`, `GitHub__ClientSecret`, and `GitHub__PrivateKeyBase64` or `GitHub__PrivateKeyPath` — existing GitHub App credentials. They remain backend-only and are never copied into owner requests, responses, or logs.
- `GitHub:RepositoryLinks:MaxLinkedRepositories` / `MaxEnabledRepositories` — current project repository-capacity rules, presently 5/5 in committed configuration. SCRUM-17 does not globally reduce these limits.

Example environment files contain placeholders only; real App secrets must stay in deployment secret storage and must not be committed.

## Frontend flow

The existing repository integration UI is preserved. `Request Access` asks the ResearchTrack requester for a GitHub repository URL, validates/normalizes it with the existing frontend GitHub URL helper, sends `{ projectId, repositoryUrl }`, and displays the backend-generated share URL, requested repository, and expiry while keeping the existing copy-link interaction. The requester does not need to sign into GitHub for request creation.

The anonymous `/github/request-access` page validates the token before enabling `Continue to GitHub`, displays only the fixed requested repository/status/expiry returned by the backend, and does not let the repository owner replace the target repository. The frontend additionally accepts only the canonical HTTPS `github.com/apps/{app}/installations/new?...` authorization URL returned by the backend.

For `INSTALLATION_REQUESTED`, `/github/access-updated` is a terminal result experience; it does not open the unrestricted installation-repository selector. `INSTALLATION_DIRECT` retains the existing repository-selection behavior. The owner-request page is served with `no-store`/`no-referrer` protections and its Nginx access log is disabled so the bearer query token is not written to the frontend web-server access log.

## Current callback security contract

The current deployed SCRUM-15 runtime no longer performs a second GitHub user OAuth/PKCE callback after the GitHub App setup callback. The earlier implementation did contain that two-leg user-to-installation verification, but commit `fc856269` intentionally removed it from the active direct flow and made user-OAuth callbacks an explicit `unexpected_user_oauth` failure. The current supported direct-flow security boundary is therefore: short-lived one-time server-side ResearchTrack state first, followed by GitHub App identity verification of the returned installation.

SCRUM-17 reuses that current contract rather than creating a parallel OAuth architecture: `INSTALLATION_REQUESTED` state is bound server-side to the persistent owner-grant request, the state is validated before `installation_id` is used, state/request are atomically pinned to one installation, and the installation is verified with the ResearchTrack GitHub App identity. If Sprint requirements later require restoring user-to-installation OAuth proof, it should be restored consistently for both direct and requested flows rather than only for SCRUM-17.

For `INSTALLATION_REQUESTED`, callback processing now proceeds from the verified installation to an exact installation-repository lookup. Only after that authoritative owner/name match succeeds does the completion boundary persist the installation-backed source, repository metadata and project link and transition the request to `COMPLETED`.

## Exact repository verification

Verification succeeds only when the installation's current GitHub response includes the requested repository. Compare normalized case-insensitive owner/name for the requested identity, and persist/operate on GitHub's stable numeric repository ID after the match. Do not accept any of these as proof:

- the original private repository URL;
- a repository identity returned by the browser;
- the callback `installation_id` by itself;
- access to some other repository in the installation;
- stale repository discovery performed before final completion.

Perform the access check again inside, or immediately before, the completion transaction to close the gap between repository selection and persistence.

## Replay protection and idempotency

- Use a conditional update/row lock so only one worker can move a valid `PENDING` request to completion.
- Mark the request consumed in the same transaction as link creation.
- Repeated successful callbacks return the already-completed safe result and do not create another source, link, or initial-sync request.
- Unknown, malformed, expired, failed, or consumed tokens cannot mutate the project connection.
- Denial and verification failures use safe error codes and never overwrite an existing active connection.
- SCRUM-17 itself always targets one exact repository per request, but it does not change the project's global repository-count policy. Enforce the configured `MaxLinkedRepositories` / `MaxEnabledRepositories`, the existing active-repository uniqueness key, and the single-primary key; handle races as a deterministic conflict/idempotent completion rather than duplicating links.

## Failure outcomes

| Condition | Request result | Active connection |
| --- | --- | --- |
| Owner denies/cancels GitHub authorization | `FAILED` | Unchanged |
| Authorized installation lacks the requested repository | `FAILED` | Unchanged |
| Callback state is malformed or does not match | Reject; fail only when the matching request can be safely identified | Unchanged |
| Request lifetime elapses | `EXPIRED` | Unchanged |
| Token/callback is replayed after completion | Return safe idempotent result or reject as consumed | No duplicate |
| Exact requested repository is already actively linked | Conflict/failure | Existing connection preserved |
| Configured project linked/enabled repository limit is reached | Conflict/failure | Existing connections preserved |
| GitHub/dependency is temporarily unavailable | Keep pending when safely retryable, otherwise `FAILED` with safe code | Unchanged |

## Synchronization handoff

Successful completion commits the verified GitHub repository ID, owner/full name, canonical URL, default branch, installation-backed source, project link, and terminal owner-request transition in one database transaction. Only the transaction that creates the link emits an `InitialRepositorySyncRequest`; an idempotent replay of an already-completed request emits no second logical initial handoff.

After commit, completion invokes the existing shared `IInitialRepositorySyncRequester`, which is registered to Story 11's `RepositorySyncQueue`. The queue already de-duplicates a linked repository while it is queued/running. The link is durably created as active, enabled, and `PENDING` with no `LastSyncedAt`; therefore, if the process fails after the database commit but before the in-memory handoff survives, the existing `RepositoryScheduledSyncWorker` will rediscover and enqueue the unsynchronized link. If the immediate handoff throws, the link is marked `FAILED` for visibility and remains eligible for the existing scheduler's recovery path. This is the current reliable recovery mechanism; SCRUM-17 does not introduce a second sync engine or claim a transactional queue/outbox that the branch does not have.

## Lifecycle hardening and sensitive logging

- `PENDING` is the only mutable owner-request lifecycle state. `COMPLETED`, `FAILED`, and `EXPIRED` are immutable terminal states.
- Expiry is materialized with conditional database updates. The installation-state/request binding transaction re-checks request expiry and, if the deadline has passed, atomically marks the request `EXPIRED` and consumes the one-time installation state before any installation can be bound.
- Failure persistence accepts only the allow-listed, non-sensitive `GitHubRepositoryAccessFailureCodes`; raw GitHub provider errors are never persisted as request failure codes.
- An already-`COMPLETED` request is considered an idempotent completion only when the replay matches the originally persisted installation and stable GitHub repository identity. A different installation/repository replay is rejected as `completion_inconsistent` without mutation.
- Public bearer-token lookup intentionally gives malformed and unknown tokens the same generic unavailable response. A valid token for an elapsed `PENDING` request materializes `EXPIRED`; terminal tokens cannot pass the pending/continue guard. Token hashes, project secrets, and credentials are not returned.
- Custom HTTP request logging records `Request.Path` rather than the query string. Because public owner-grant tokens and GitHub callback state/code currently travel as query parameters on established routes, Gateway YARP forwarding logs are set to `Warning` so the information-level proxy target URL cannot record those query values.
- Application audit events may contain safe identifiers (`RequestId`, `ProjectId`, `InstallationId`, stable GitHub repository ID) plus allow-listed lifecycle/failure codes. They must never contain raw owner-grant tokens, raw ResearchTrack state, GitHub authorization codes, GitHub user/access/installation tokens, App JWTs, client secrets, or private keys.

## Acceptance criteria mapping

| Criterion | Backend evidence |
| --- | --- |
| AC1 | Authorized create operation persists a `PENDING` request bound to project, requester, and exact repository. |
| AC2 | Hashed high-entropy token/state, configured expiry, backend-generated URL, and no credential exposure. |
| AC3 | Callback resolves and validates only persisted server-side request context. |
| AC4 | Installation API response contains the exact normalized requested repository and supplies its stable ID/metadata. |
| AC5 | Wrong repository, denial, expiry, dependency failure, and policy conflict never insert/replace an active link. |
| AC6 | One transaction persists verified metadata/link and `COMPLETED`; shared initial synchronization is handed off after commit. |
| AC7 | Conditional lifecycle checks reject malformed, expired, consumed, and replayed values without connection mutation. |

## Automated test requirements

- Authorized requester creates a pending request for the correct project and normalized repository.
- Unauthorized/non-member request creation returns the standard `401`/`403` response.
- Raw request tokens and GitHub credentials are not persisted or included in responses/log assertions.
- Valid token validation returns safe context; malformed, unknown, and expired tokens fail safely.
- Callback recovers persisted context and rejects mismatched/tampered state.
- Exact repository succeeds; different repository, missing repository, renamed/mismatched identity, and revoked access fail without a link.
- Successful completion persists the numeric repository ID, canonical metadata, and default branch.
- Duplicate callbacks and concurrent completions create one link and one logical initial-sync handoff.
- Existing active project connection wins a race and remains unchanged.
- Denied, failed, expired, and replayed flows leave the project repository state unchanged.

## Manual QA

Use non-production GitHub App credentials and a disposable private repository. Recommended final QA sequence:

1. As an authorized ResearchTrack supervisor/project manager, open the existing repository integration modal, select `Request Access`, enter `https://github.com/<owner>/<repository>`, generate the request, copy the share URL, and confirm the UI shows the normalized repository and expiry.
2. Open the share URL in a signed-out/incognito browser. Confirm the page displays only the fixed repository/status/expiry and cannot edit the repository. Continue and confirm the redirect is the configured GitHub App installation page.
3. Complete GitHub App authorization with an account that may administer the requested repository. Confirm the callback result says the exact requested repository was linked and does not show an unrestricted repository picker.
4. Back in ResearchTrack, confirm one active repository link exists with GitHub's numeric repository ID, canonical URL/full name, and default branch, and confirm the existing Story 11 synchronization starts/records a sync attempt.
5. Repeat with an installation that cannot access the requested repository; deny/cancel the GitHub flow; allow a request to expire; tamper with the owner token/state; replay the callback; and revoke repository access before completion. None of these cases may create or replace an active project connection.
6. Test a project that already has another repository linked. Confirm a distinct repository may still complete while configured capacity remains, while an exact duplicate or capacity race fails without changing the existing connection.
7. Inspect `github_repository_access_requests`, installation state, access-source/repository/link rows, application logs, Gateway logs, and frontend web-server logs. Confirm one logical successful completion/sync handoff and no raw owner token, raw ResearchTrack state, GitHub authorization code/token, App JWT, client secret, or private key is stored or logged.

## Dependencies and exclusions

This flow depends on Research Project creation/authorization, the hardened GitHub App authorization capability from SCRUM-15/Story 9, and Story 11 synchronization. It does not bypass GitHub permissions or organization policy, distribute PATs, grant GitHub roles, or treat a private URL as authorization.
