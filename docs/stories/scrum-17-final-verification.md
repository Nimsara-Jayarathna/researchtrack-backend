# SCRUM-17 / US-203 — final technical verification

Audit date: 2026-09-14. Both CURRENT working trees on `feature/SCRUM-18-us-204-synchronize-gitHub-repository-activity` were reviewed. Baseline backend HEAD `bb6eaed`; frontend HEAD `8a5abc0`. No branch reset, old archive extraction, commits, pushes, or production migration was performed. There were no initial untracked implementation files. Four pre-existing Grafana deletions were preserved and are absent from the delivery because they are absent from the current tree.

## 1. Final verdict

**PASS WITH ENVIRONMENT LIMITATIONS**

All executable code/build/database tests passed after targeted fixes. Real GitHub authorization and deployed proxy observations remain manual QA. The supported SCRUM-15 security architecture remains App-authenticated installation verification without a separate user-OAuth proof.

## 2. Problems found and fixed

| Severity | Issue | Files / fix |
| --- | --- | --- |
| High | Completion used a caller timestamp captured before lock/database waits. | `GitHubRepositoryAccessCompletionStore` now refreshes time after the lock/read and before saving; expired staged connections are discarded. Requested state creation/binding also refreshes time after reads in `GitHubInstallationStateStore`; optimistic conflicts return safe rejection. |
| Medium | Enumeration could return metadata for a repository revoked/renamed before completion. | `GitHubInstallationRepositoryService` refreshes the exact numeric ID using the existing GitHub installation client, rejects changed owner/name, and uses refreshed metadata. |
| High | EF snapshot omitted existing owner-request and synchronization schema. | EF-generated reconciliation designer/snapshot, with reviewed migration operations limited to ten index renames. No duplicate table creation or historical migration replacement. |
| Medium | MySQL timestamps could serialize without UTC and display the wrong owner-link expiry. | Request status, token validation and continuation services explicitly return UTC expiry; serialization regression added. |
| Medium | Requested result trusted query success, ignored non-completed authenticated status, and failed returns with project context could display an unrelated summary. | Requested result hook/page now use informational anonymous copy, verify authenticated completion before a linked claim, and suppress unrelated summary/selection. |
| Low | Promise-returning create helper threw synchronously on invalid URL; a UI test used an ambiguous text match. | Made the helper async; assert the unique result heading and add failure/tampering regressions. |
| Medium | Owner-request integration tests reset the same database concurrently with other suites. | Isolated owner-grant collection; intentional concurrent completion/capacity tests remain concurrent. |
| Low | Frontend documentation described nonexistent public completion/acknowledgement endpoints and automatic status behavior. | Corrected both story documents to the actual API and UI contract. |

## 3. Backend files changed during this pass

- `docs/stories/owner-granted-github-repository-access.md`
- `docs/stories/scrum-17-final-verification.md`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationRepositoryService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessContinuationService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessRequestService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessTokenService.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/GitHubInstallationStateStore.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/GitHubRepositoryAccessCompletionStore.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20260914113212_ReconcileGitHubModelSnapshot.Designer.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20260914113212_ReconcileGitHubModelSnapshot.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/GitHubDbContextModelSnapshot.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubInstallationRepositoryServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubRepositoryAccessTokenServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Integration/OwnerGrantedRepositoryCompletionPersistenceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Integration/PublicAccessSourceAuthorizationTests.cs`

## 4. Frontend files changed during this pass

- `docs/features/owner-granted-github-access.md`
- `src/features/supervisor/api/supervisorGitHubApi.ts`
- `src/features/supervisor/hooks/githubAccessUpdated/useGitHubAccessUpdatedSummaryState.ts`
- `src/features/supervisor/hooks/useGitHubAccessUpdatedPageState.ts`
- `src/features/supervisor/pages/GitHubAccessUpdatedPage.test.tsx`
- `src/features/supervisor/pages/GitHubAccessUpdatedPage.tsx`

## 5. Migration status

Current model and generated snapshot agree (`has-pending-model-changes`: no changes). All migrations applied successfully to disposable local MySQL on port 33317. A database integration test checks every model index by name against `information_schema.statistics`. The added migration is `20260914113212_ReconcileGitHubModelSnapshot`; its reversible operations rename the ten pre-existing sync indexes to current model conventions, preserving existing schema/data. Earlier handwritten migration IDs remain unchanged. An idempotent migration script was generated and inspected; no production database was used.

Persistent owner request: project/user/exact normalized owner/name/full name, numeric repository identity after verification, SHA-256 token hash, flow/status, creation/expiry/start/binding/completion/consumption timestamps, safe failure code, optimistic `Version`. Unique token/state hashes, request status+expiry/project indexes, state-to-request FK/index, active installation and active project/repository uniqueness, and source+numeric repository uniqueness exist. Project/user references stay in their owning service; no cross-database foreign keys are invented.

## 6. API verification

Both `/api/github/...` and `/api/v1/github/...` are exposed. The frontend owner request calls unversioned routes.

| Method | Actual route | Boundary |
| --- | --- | --- |
| POST | `/api/github/access-requests` | Supervisor JWT/cookie plus existing Project Service authorization; `{projectId, repositoryUrl}` required |
| GET | `/api/github/access-requests/{requestId:guid}` | Authorized original requester; safe status only |
| GET | `/api/github/access-requests/validate?token=...` | Anonymous narrow bearer validation |
| POST | `/api/github/access-requests/continue?token=...` | Anonymous pending bearer; server-bound installation state |
| GET | `/api/github/access-source/install/callback` | Existing shared callback; validated state determines direct/requested context |
| POST | `/api/github/access-source/install/start` | Existing authenticated direct flow, preserved |

No separate SCRUM-17 completion-summary endpoint exists. The anonymous result is display-only and cannot mutate repository links.

## 7. AC1–AC7 matrix

Runtime PASS below means executed automated tests, using real MySQL for persistence and fake GitHub responses for provider interactions. It does not mean a live GitHub authorization was performed.

| AC | Implementation | Automated Test | Runtime Result |
| --- | --- | --- | --- |
| AC1 | Authenticated owning supervisor; normalized exact repository; limits; PENDING | `GitHubRepositoryAccessRequestServiceTests`, HTTP role tests in `PublicAccessSourceAuthorizationTests`, frontend API/modal tests | PASS |
| AC2 | 32 random bytes, SHA-256 only, short expiry, trusted URL, safe status responses | Request/token/state suites; explicit 32-byte/hash checks, secret response/log assertions, UTC serialization | PASS |
| AC3 | Existing callback recovers stored project/user/request; context/state/lifecycle checks before installation use | `GitHubInstallationFlowServiceTests`, `GitHubInstallationStateServiceTests`, continuation suite | PASS |
| AC4 | Installation token; exact case-insensitive owner/name; final numeric-ID refresh | Requested repository tests: exact success, different repository failure, revoked/renamed after enumeration | PASS |
| AC5 | Denial, invalid installation/state, expiry, provider failure, wrong repository, duplicates/capacity/source conflicts cannot insert a connection | Flow suite plus owner completion persistence failure tests | PASS |
| AC6 | Atomic source/metadata/link/COMPLETED; shared sync queue; scheduled recovery | `OwnerGrantedRepositoryCompletionPersistenceTests`, completion service suite, scheduler crash-window regression | PASS |
| AC7 | Terminal request immutability, one-time state, optimistic version and project DB lock | Token/state replay, duplicate callback, concurrent completion and capacity race, changed-identity replay, expiry during transaction | PASS |

## 8. Security review

- **Auth:** SupervisorOnly plus Project Service `GetAccessibleProjectAsync` restricts supervisors to `SupervisorUserId`. Request status also checks initiating user. The public owner has only token-scoped access and cannot choose a repository.
- **Bearer:** 256-bit CSPRNG; SHA-256 persisted; configured 1–30 minute expiry; malformed/unknown generic unavailable; terminal status cannot Continue. Creation is the one intentional response carrying the bearer share URL.
- **State:** 32 random bytes, hashed storage, one-time conditional consume, server-side request/project/user binding. State/request installation binding is transactional and protected by request concurrency version. Failures cannot rewrite completed requests.
- **GitHub trust:** Installation lookup uses App JWT; repository listing and final GET use backend installation token. Exact persisted owner/name must match. Stable numeric identity and authoritative URL/full name/default branch are persisted. User OAuth/PKCE clients remain outside the live flow, as in commit `fc856269`. This contract does not independently prove browser-user ownership of the supplied installation ID; no stronger user-proof claim is made.
- **SSRF:** Existing GitHub parser permits the expected HTTPS GitHub repository form, rejects credentials/ports/query/fragment; GitHub clients use the fixed API origin with redirects disabled.
- **Redirects:** Frontend origin from trusted configuration; install URL fixed to GitHub App route; frontend host/path/query allowlist. Requested project navigation is taken from authenticated status, not the query.
- **DB/idempotency:** Per-project MySQL named lock shared with normal linking; transaction, unique indexes and request version. Sequential callback replay is rejected; persistence reentry for the same identity returns existing completion. Different identity fails without mutation. Existing 5/5 capacity preserved.
- **Sync:** Existing `IInitialRepositorySyncRequester` → `RepositorySyncQueue`; only a new successful link emits the logical initial request. Scheduler recovers durable active/enabled unsynced rows if enqueue is lost. Tested with the real queue/scheduler boundary; no real GitHub sync was executed.
- **Logging/secrets:** Application request logs use path without query; ASP.NET request logging and YARP forwarding are suppressed at sensitive levels. Deployed `nginx/nginx.conf` disables owner-link access logging and sends no-store/no-referrer. API client diagnostic paths redact bearer/state/code. Scanned tracked and new source/docs for private keys, GitHub tokens, AWS IDs and JWT literals; only an explicit fake private-key test fixture matched. No real secrets were found. Deployment-level log overrides remain manual QA.

Reference: [GitHub installation authentication](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/authenticating-as-a-github-app-installation); [EF migration management](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing).

## 9. Commands executed and results

Before edits, in each repository: `git branch --show-current`, `git status --short`, `git status`, `git diff`, `git diff --stat`, `git log --oneline -10`. Read actual implementation and inspected SCRUM-15 history and both story histories; commit titles were not treated as implementation evidence.

| Command | Result |
| --- | --- |
| `dotnet --info`; `cat global.json` | SDK 10.0.400 allowed by 10.0.300/latestFeature policy |
| `dotnet restore ResearchTrack.sln` | PASS |
| `dotnet build ResearchTrack.sln --no-restore` | PASS; zero warnings/errors |
| `./scripts/build.sh` | PASS after final fixes; zero warnings/errors |
| `dotnet test ResearchTrack.sln --no-build --filter 'Category!=DatabaseIntegration' --logger 'trx;LogFileName=unit.trx'` | Initial baseline: 226 passed |
| `dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj --logger 'trx;LogFileName=github-full.trx'` | Earlier full run: 209 passed; later tests added |
| `dotnet test ResearchTrack.sln --no-build --logger 'trx;LogFileName=final-solution.trx'` | FINAL: 306 passed, zero failures/skips; test-only connection variables described below |
| `./scripts/test.sh github` | PASS: 193 non-database GitHub tests, coverage collected |
| `./scripts/test.sh integration` | Wrapper failed because all six service `.env.local` files are absent; equivalent actual DB tests passed in final full-solution run |
| `dotnet ef migrations list --project src/Services/ResearchTrack.GitHubService --no-build --no-connect` | Eight migration IDs discovered; does not claim deployment application status |
| `dotnet ef migrations has-pending-model-changes --project src/Services/ResearchTrack.GitHubService --no-build` | Initially failed; PASS after repair |
| `dotnet ef migrations add ReconcileGitHubModelSnapshot --project src/Services/ResearchTrack.GitHubService --no-build` | Generated designer/snapshot; reviewed operations to avoid duplicating existing handwritten schema |
| `dotnet ef migrations remove --project src/Services/ResearchTrack.GitHubService --offline` | Unsupported option; no removal occurred; not used for the repair |
| `dotnet ef migrations script --idempotent --project src/Services/ResearchTrack.GitHubService --no-build --output /tmp/researchtrack-scrum17-migrations.sql` | PASS |
| `node --version`; `npm --version` | Node 24.18.0; npm 11.16.0 |
| `npm ci` | PASS; lockfile unchanged |
| `npm run typecheck` | PASS |
| `npm run lint` | PASS, zero warnings |
| `npm test` | Initially 423 pass/2 fail; FINAL 429 pass across 99 files |
| `npm run build` | PASS; existing large-bundle advisory, no build failure |
| `npm audit --json` | One existing high-severity development-only js-yaml advisory; dependency versions unchanged |
| `npm audit --omit=dev --json` | Zero production dependency findings |
| `git diff --check` (both repositories) | PASS |

The final solution test process used these six variables, each pointing only at the isolated disposable server:

```text
RESEARCHTRACK_TEST_<AUTH|PROJECT|GITHUB|JIRA|MEETING|SUBMISSION>_CONNECTION=
Server=127.0.0.1;Port=33317;Database=researchtrack_test_<service>_scrum17;User=root;Password=;SslMode=Disabled;AllowPublicKeyRetrieval=true
```

This server was newly initialized in a temporary directory, bound only to loopback, used only for fake test data, and stopped after verification. It did not use the existing local MySQL service or production credentials.

Final test counts:

- ResearchTrack.AuthService.Tests: 34 passed, 0 failed.
- ResearchTrack.Gateway.Tests: 7 passed, 0 failed.
- ResearchTrack.GitHubService.Tests: 216 passed, 0 failed.
- ResearchTrack.JiraService.Tests: 4 passed, 0 failed.
- ResearchTrack.MeetingService.Tests: 4 passed, 0 failed.
- ResearchTrack.ProjectService.Tests: 37 passed, 0 failed.
- ResearchTrack.SubmissionService.Tests: 4 passed, 0 failed.

Browser verification: `npm run dev -- --host 127.0.0.1 --port 4173`, then `npx --yes agent-browser --session scrum17 open ...`, `snapshot -i`, screenshots and `errors`. Verified missing-token owner page (Continue disabled), pending exact repository (mocked validation response; Continue enabled), forged requested-result query (informational; no selector/linked proof), home navigation, and no browser exceptions/Vite overlay. Desktop screenshots visually inspected. No live provider authorization was attempted. The initial browser-tool fetch timed out; retry succeeded.

## 10. Manual GitHub QA still required

1. Configure a non-production GitHub App, trusted callback/origin and Project Service auth; use a disposable private repository.
2. As its ResearchTrack owning supervisor, generate/copy a request with the exact URL; open signed out and confirm fixed repository and UTC-correct local expiry.
3. Grant the exact repository on GitHub. Reopen the original link and confirm COMPLETED; refresh the project and check numeric ID, canonical metadata/default branch, and initial sync evidence.
4. Repeat with only a different repository, denial/cancellation, expired link, invalid state, duplicate callback, and revocation before completion. Existing active links must remain unchanged.
5. Repeat with another repository already linked, at configured capacity, and with concurrent completion attempts. Check one logical new link/sync handoff.
6. Exercise direct App installation and its existing repository selector.
7. Inspect deployed API/gateway/frontend/proxy logs and DB rows for sensitive values, and verify deployed headers; verify scheduler recovery on a restarted instance.

## 11. Limitations and pre-existing state

- Live GitHub authorization, organization policy and provider synchronization were not exercised; automated provider tests use fakes. Deployed proxy configuration/overrides were not observed.
- The integration script wrapper requires local service env files; the actual suites passed with explicit isolated test connections.
- Existing development-only js-yaml advisory and production bundle-size warning remain outside SCRUM-17 changes; production dependency audit is clean.
- Four pre-existing Grafana deletions were preserved: `config/env/grafana/.env.example`, `deploy/monitoring/grafana/provisioning/dashboards/dashboards.yml`, `deploy/monitoring/grafana/provisioning/dashboards/json/researchtrack-overview.json`, and `deploy/monitoring/grafana/provisioning/datasources/prometheus.yml`. This package reflects the latest working tree, not a restoration of those deployment files.
- The inherited App-only installation contract has no separate browser-user OAuth proof, as explicitly documented above.

## 12. Final ZIP

`ResearchTrack-SCRUM-17-Owner-Granted-Access-FINAL-CODEX-VERIFIED.zip` in the workspace root. Package manifest and SHA-256 are delivered alongside it. Packaging reads the current repositories and includes untracked new source/migration/documentation files. No `.git`, dependency/build/test-output directories, local env files, private keys, old ZIP, or OS junk are included. Archive integrity, required files, exclusions and every archived file hash are verified after creation.

## 13. Recommended commit message

`fix(github): harden and verify SCRUM-17 owner-granted repository access`

This is a targeted final verification/fix pass over an existing implementation.

## 14. Recommended PR title

`SCRUM-17 / US-203: Link repository through owner-granted GitHub access`

## Definition of done

Checked items represent source inspection plus executed automated tests/browser checks, within the live-GitHub limitation above.

- [x] AC1
- [x] AC2
- [x] AC3
- [x] AC4
- [x] AC5
- [x] AC6
- [x] AC7
- [x] pending lifecycle implemented
- [x] secure/time-bounded/replay-protected owner link
- [x] exact repository verification
- [x] wrong repo cannot create link
- [x] denial cannot create link
- [x] expiry cannot create link
- [x] successful completion persists repository ID
- [x] successful completion persists canonical metadata
- [x] successful completion persists default branch
- [x] successful completion marks request COMPLETED
- [x] existing shared initial synchronization starts
- [x] duplicate callback is idempotent
- [x] concurrent completion is safe
- [x] existing active project connection survives failures
- [x] direct GitHub App flow remains regression-free
- [x] frontend remains visually consistent
- [x] public owner flow cannot select another repository
- [x] no secret/token leakage
- [x] documentation matches implementation
- [x] backend build passes
- [x] backend tests pass
- [x] frontend typecheck passes
- [x] frontend lint passes
- [x] frontend tests pass
- [x] frontend production build passes

- [ ] Live non-production GitHub owner authorization and initial provider sync: requires configured App/account/repository; follow manual QA above.
- [ ] Deployed proxy log/header observation: no deployed environment was provided.

## Inspected SCRUM-17 implementation inventory

The following current source/doc/test files contain owner-request identifiers or the requested flow. Shared URL/auth/link/sync/GitHub client infrastructure and gateway/frontend configuration were also inspected as described above.


### researchtrack-backend

- `docs/stories/owner-granted-github-repository-access.md`
- `src/Services/ResearchTrack.GitHubService/Contracts/GitHubRepositoryAccessRequestContinueResponse.cs`
- `src/Services/ResearchTrack.GitHubService/Contracts/GitHubRepositoryAccessRequestContracts.cs`
- `src/Services/ResearchTrack.GitHubService/Contracts/GitHubRepositoryAccessRequestStatusResponse.cs`
- `src/Services/ResearchTrack.GitHubService/Controllers/GitHubInstallationController.cs`
- `src/Services/ResearchTrack.GitHubService/Controllers/GitHubRepositoryAccessRequestsController.cs`
- `src/Services/ResearchTrack.GitHubService/Domain/GitHubAccessTypes.cs`
- `src/Services/ResearchTrack.GitHubService/Domain/GitHubInstallationFlowState.cs`
- `src/Services/ResearchTrack.GitHubService/Domain/GitHubInstallationFlowTypes.cs`
- `src/Services/ResearchTrack.GitHubService/Domain/GitHubRepositoryAccessRequest.cs`
- `src/Services/ResearchTrack.GitHubService/Domain/GitHubRepositoryAccessRequestStatuses.cs`
- `src/Services/ResearchTrack.GitHubService/Extensions/GitHubFeatureExtensions.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationCallbackResult.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationFlowService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationRepositoryService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationStateModels.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubInstallationStateService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessContinuationService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessRequestService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/GitHubRepositoryAccessTokenService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/IGitHubRepositoryAccessContinuationService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/IGitHubRepositoryAccessRequestService.cs`
- `src/Services/ResearchTrack.GitHubService/Features/Installation/IGitHubRepositoryAccessTokenService.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/GitHubInstallationStateStore.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/GitHubRepositoryAccessCompletionStore.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/GitHubRepositoryAccessRequestStore.cs`
- `src/Services/ResearchTrack.GitHubService/Infrastructure/IGitHubRepositoryAccessRequestStore.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Configurations/GitHubInstallationFlowStateConfiguration.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Configurations/GitHubRepositoryAccessRequestConfiguration.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/GitHubDbContext.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/20260914064000_AddOwnerGrantedRepositoryAccessRequests.cs`
- `src/Services/ResearchTrack.GitHubService/Persistence/Migrations/GitHubDbContextModelSnapshot.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubInstallationFlowServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubInstallationStateServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubRepositoryAccessContinuationServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubRepositoryAccessRequestServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Features/Installation/GitHubRepositoryAccessTokenServiceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Integration/OwnerGrantedRepositoryCompletionPersistenceTests.cs`
- `tests/ResearchTrack.GitHubService.Tests/Integration/PublicAccessSourceAuthorizationTests.cs`

### researchtrack-web

- `README.md`
- `docs/features/owner-granted-github-access.md`
- `src/features/shared/types/github.types.ts`
- `src/features/supervisor/api/supervisorGitHubApi.test.ts`
- `src/features/supervisor/api/supervisorGitHubApi.ts`
- `src/features/supervisor/components/ProjectDetail/IntegrationsTabSection.tsx`
- `src/features/supervisor/components/ProjectDetail/RepositoryLinkModalContent.test.tsx`
- `src/features/supervisor/components/ProjectDetail/RepositoryLinkModalContent.tsx`
- `src/features/supervisor/components/ProjectDetail/RepositorySection.tsx`
- `src/features/supervisor/hooks/projectDetails/useSupervisorProjectGitHubSetupRedirect.ts`
- `src/features/supervisor/hooks/useGitHubAccessUpdatedPageState.ts`
- `src/features/supervisor/hooks/useGitHubSetupFlow.ts`
- `src/features/supervisor/pages/GitHubAccessUpdatedPage.test.tsx`
- `src/features/supervisor/pages/RequestGitHubRepositoryAccessPage.test.tsx`
- `src/features/supervisor/pages/RequestGitHubRepositoryAccessPage.tsx`
- `src/features/supervisor/types/github.types.ts`
