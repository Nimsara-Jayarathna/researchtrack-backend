# Owner-granted GitHub repository access

## Story contract

An authorized ResearchTrack project member who cannot manage GitHub App access may create a pending request for one exact GitHub repository. A repository owner or administrator follows a time-bounded authorization URL. ResearchTrack recovers the server-side request context, verifies that the resulting GitHub App installation can access that exact repository, and only then creates the active project-to-repository connection and requests initial synchronization.

The request and active connection are separate records. Creating a request, receiving an `installation_id`, or accepting a private repository URL must not create an active connection.

## Security boundary

- GitHub credentials remain between the repository owner, GitHub, and backend-only GitHub App clients.
- The requester and browser never receive or store the owner's GitHub password, PAT, GitHub user token, installation token, App private key, or OAuth client secret.
- Shareable request tokens are high-entropy, opaque, time-bounded, single-use bearer values. Persist only a cryptographic hash of each token.
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
3. Confirm that adding a connection is allowed under the current one-active-connection-per-project rule.
4. Create `PENDING` request state, generate a high-entropy one-time token, persist only its hash, and return the shareable frontend URL plus expiry.
5. When the owner opens the URL, hash and look up the token, validate status/expiry, and return only safe project/repository context.
6. On continue, atomically reserve/advance the pending flow and generate the GitHub App setup/authorization URL bound to the request. Reuse the hardened App installation and GitHub user authorization checks established by SCRUM-15.
7. On callback, validate the ResearchTrack state before using GitHub's `installation_id`; recover the original project, requester, and requested repository from storage.
8. Verify the installation with the ResearchTrack GitHub App identity and confirm the authorizing GitHub user may act on that installation according to the supported GitHub App flow.
9. List or retrieve installation repositories with a short-lived installation token. Match the requested canonical owner/name, then confirm the stable repository ID and fetch current canonical URL/default branch from GitHub. A repository with a different owner/name is a failed outcome, even if it belongs to the same installation.
10. In an idempotent transaction, re-check that the request is still pending and unexpired, enforce the project's single active repository constraint, persist the installation-backed access source and exact repository metadata/link, mark the request `COMPLETED`, and record consumption.
11. After durable completion, invoke the shared initial synchronization boundary once for the created link. A transactional outbox or an equivalently reliable handoff is preferred so a process failure cannot leave a completed link permanently unsynchronized.

## API responsibilities

The concrete route version should follow existing Gateway conventions. Compatibility aliases may remain, but the owner-granted contract needs the following behaviors:

| Operation | Authorization | Result |
| --- | --- | --- |
| Create owner-granted request | Authenticated authorized project member | `201`; request reference/URL, `PENDING`, expiry, and safe requested-repository summary. |
| Get member-visible request status | Authenticated authorized project member | Current lifecycle state and safe completion/failure summary. |
| Validate public request | Opaque request token | Safe project/repository context, state, and expiry only. |
| Continue to GitHub | Opaque request token | Backend-generated GitHub URL after atomic pending/expiry checks. |
| GitHub setup/OAuth callback | GitHub redirect plus bound state | Server-side validation and safe redirect to the frontend result route. |
| Get public result | Opaque result/request token | Backend-derived terminal or pending outcome; no credentials. |

The create payload must include the requested repository URL or canonical full name in addition to the project ID. Existing project-only access-request contracts do not satisfy exact-repository binding.

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
- Enforce the one-active-connection rule at both the service layer and database level where supported; handle uniqueness races as conflict/idempotent completion rather than duplicating links.

## Failure outcomes

| Condition | Request result | Active connection |
| --- | --- | --- |
| Owner denies/cancels GitHub authorization | `FAILED` | Unchanged |
| Authorized installation lacks the requested repository | `FAILED` | Unchanged |
| Callback state is malformed or does not match | Reject; fail only when the matching request can be safely identified | Unchanged |
| Request lifetime elapses | `EXPIRED` | Unchanged |
| Token/callback is replayed after completion | Return safe idempotent result or reject as consumed | No duplicate |
| Project already has a different active connection | Conflict/failure | Existing connection preserved |
| GitHub/dependency is temporarily unavailable | Keep pending when safely retryable, otherwise `FAILED` with safe code | Unchanged |

## Synchronization handoff

Successful completion persists the verified GitHub repository ID, owner/full name, canonical URL, default branch, installation-backed source, and project link before requesting initial synchronization. Use the shared Story 11 synchronization path; this story must not create a second sync engine.

If the deployed branch still uses a deferred `IInitialRepositorySyncRequester`, the access flow may record the handoff but must not claim that ingestion completed. Production completion of AC6 requires the real Story 11 requester/queue integration.

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

Use a private test repository and a GitHub account that can administer the App installation. Verify the happy path, wrong-repository selection, denial, expiry, token tampering, callback replay, revoked access before completion, and an already-connected project. Inspect the GitHub Service database and logs to confirm one completed link/handoff and no stored or logged credentials.

## Dependencies and exclusions

This flow depends on Research Project creation/authorization, the hardened GitHub App authorization capability from SCRUM-15/Story 9, and Story 11 synchronization. It does not bypass GitHub permissions or organization policy, distribute PATs, grant GitHub roles, or treat a private URL as authorization.
