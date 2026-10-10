# ResearchTrack project-wide mutation strategy

## Objective

Mutation testing is used as a test-effectiveness technique, not as a raw code-coverage replacement. The selected scope targets places where a mutation can change externally meaningful behavior: validation boundaries, authorization decisions, security checks, state transitions, workflow rules, synchronization decisions, and orchestration side effects.

The following are deliberately not used to inflate the mutation denominator: controllers with no business decisions, DTO-only contracts, EF migrations, generated code, bootstrap code, logging-only statements, and routine HTTP/persistence plumbing.

## Service scopes

### AuthService
Password policy, authentication/session rotation, password reset, registration, user account/session revocation, and user directory behavior. Auth has already completed survivor hardening and is kept as the established reference scope.

### ProjectService
- `ProjectMilestonePolicy`
- `ProjectRequestValidator`
- `ProjectService`
- `SupervisorDashboardService`

This expands Project beyond deterministic rules into ownership, membership, leader assignment, milestone mutation, and dashboard aggregation/orchestration behavior.

### SubmissionService
- `SubmissionFileRules`
- `SubmissionResponsibilityRules`
- `SubmissionConstants` review rules
- `SubmissionRequirementService`
- `ResearchSubmissionService`

This captures file/type/size boundaries, responsibility authorization, requirement lifecycle, upload/version/review workflow, and persistence side effects.

### GitHubService
- repository URL parsing
- webhook HMAC signature verification
- access-request token construction/verification
- installation state lifecycle
- repository linking orchestration
- webhook event processing
- reconciliation decisions

Transport-only GitHub API clients are intentionally excluded from the core mutation score because their correctness is primarily contract/integration-oriented rather than branch-heavy business policy.

### JiraService
- issue mapping
- sync-state transitions
- issue query/health aggregation
- sprint-progress aggregation
- webhook authentication/routing/scheduling

### Gateway
- performance-test rate-limit bypass policy
- trusted proxy forwarding configuration

These are security-sensitive policies and therefore high-value mutation targets despite the small code volume.

### MeetingService
- meeting-channel lifecycle/authorization rules
- meeting-record lifecycle/authorization rules

## Dedicated harness model

Normal service tests use the repository's current xUnit v3 setup. Stryker runs through dedicated xUnit v2 harness projects because that runner path has been empirically validated in this repository to observe injected production mutations correctly. The harnesses reuse focused test logic but replace xUnit v3 `TestContext.Current.CancellationToken` with `CancellationToken.None` so they remain deterministic and independent of database integration infrastructure.

Every mutation run first verifies:

1. the normal unit/mock project with `Category!=DatabaseIntegration`; and
2. the dedicated mutation harness.

Only then does Stryker mutate production code.

## Execution

Run one service while hardening survivors:

```bash
bash ./scripts/mutation-baseline.sh project
bash ./scripts/mutation-baseline.sh submission
bash ./scripts/mutation-baseline.sh github
bash ./scripts/mutation-baseline.sh jira
bash ./scripts/mutation-baseline.sh gateway
bash ./scripts/mutation-baseline.sh meeting
```

Run the complete selected business-logic portfolio only when individual harnesses are stable:

```bash
bash ./scripts/mutation-baseline.sh all
```

The manual GitHub Actions workflow exposes the same scopes and uploads HTML, JSON, Markdown, and the weighted project summary.

## Threshold policy

All newly introduced scopes start with Stryker `break = 0`. This is intentional. A regression threshold is evidence-based only after a valid baseline is obtained, meaningful survivors are hardened, and equivalent/non-business-value survivors are documented. Service and project-wide gates should be set below the stable final weighted score, not chosen before measurement.

## Survivor-review priority

Review surviving mutants in this order:

1. authorization/security decisions;
2. token/signature/identity/ownership checks;
3. state transitions and workflow gates;
4. boundary comparisons (`<`, `<=`, `>`, `>=`);
5. boolean/equality mutations (`&&`/`||`, `==`/`!=`);
6. persistence side-effect removals;
7. ordering/aggregation behavior;
8. user-facing strings/logging only after business behavior is covered.

Meaningful survivors should strengthen the normal xUnit suite first and then the dedicated mutation harness. Equivalent or non-business-value mutants should be documented rather than hidden with broad mutator exclusions.
