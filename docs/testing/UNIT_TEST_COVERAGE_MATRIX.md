# Unit Test Coverage Matrix

This matrix describes the **implemented test surface**, not a fabricated line-coverage percentage. Runtime line/branch coverage must be measured with coverlet after the suite executes in a .NET 10 environment.

| Service | Pure/unit coverage currently present | New coverage in this revision | Next mocking-stage priority |
|---|---|---|---|
| Auth | Password policy, registration validation, DB config plus integration paths | Password boundaries and password-policy configuration | Authentication, registration, reset, account/directory service interactions |
| Project | Mostly integration/authorization before this revision | **Project request validation + milestone policy + aggregate calculations** | Project persistence, user-directory authorization, dashboard orchestration |
| Submission | File/workflow/responsibility rules | **File boundaries + MIME rules + assignment/authorization edge cases** | S3/object-store flow, requirement persistence, upload completion, cleanup |
| Meeting | Channels, records, contract validation, student authorization | Existing suite retained | Dependency interaction verification only where valuable |
| GitHub | Strong configuration, installation, webhook, sync-client, app-client and link coverage | Existing suite retained; no duplicate tests added | Synchronization orchestration, reconciliation, webhook processing interactions |
| Jira | Strong mapper/query/sprint/workload/sync-state/webhook/token coverage | Existing suite retained | Sync/connection orchestration and scheduler/worker dependency interactions |
| Gateway | Rate-limit policy, trusted proxy, infrastructure smoke | Existing suite retained | Only add mocks if a new injectable policy dependency is introduced |

## Risk-based priorities

### P0 — must be mutation-strong

- Project validation and milestone transitions
- Submission responsibility/authorization rules
- Submission file rules
- Password policy
- GitHub token/signature/state validation
- Jira sync-state transitions

### P1 — mock in Phase 2

- Submission orchestration
- Project orchestration
- Auth orchestration
- GitHub sync/reconciliation/webhook event processing
- Jira sync/connection orchestration

### P2 — integration/contract focus

- Controllers
- EF persistence
- HTTP clients
- background workers
- gateway middleware/wiring

### Exclude from artificial coverage targets

- generated migrations
- `Program.cs` / startup-only wiring
- DTO/property-only models
- constants-only classes
- generated code

## Quality gates for the unit-testing workstream

Before calling Unit Testing & Mocking complete:

- all unit tests pass
- all existing integration tests still pass
- no new test requires a live external service
- important boundary cases are parameterized
- negative paths are present for each business rule
- mock interaction tests exist for external dependencies
- coverlet report is generated
- mutation test baseline is generated
- surviving mutants are reviewed and high-value survivors receive stronger tests
