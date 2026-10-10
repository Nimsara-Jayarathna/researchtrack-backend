# ResearchTrack mutation testing

ResearchTrack uses Stryker.NET through repository-local tooling and dedicated xUnit v2 mutation harnesses. Normal production-facing test projects remain on the repository's xUnit v3 stack.

The selected mutation portfolio now spans every backend business area:

- AuthService — authentication, reset, registration, account/session, directory and password policy
- ProjectService — request/milestone rules, project orchestration and dashboard aggregation
- SubmissionService — file/responsibility/review rules, requirement lifecycle and submission workflow
- GitHubService — repository parsing, security tokens/signatures, installation state, repository linking, webhook processing and reconciliation
- JiraService — issue mapping, synchronization state, issue/health queries, sprint progress and webhook routing
- Gateway — performance bypass/rate-limit policy and trusted-proxy forwarding
- MeetingService — channel and meeting-record lifecycle/authorization

The detailed rationale, commands, scope boundaries, threshold policy and survivor-priority model are in `docs/testing/mutation/project-wide/PROJECT_WIDE_MUTATION_STRATEGY.md`.

## Commands

```bash
bash ./scripts/mutation-baseline.sh auth
bash ./scripts/mutation-baseline.sh project
bash ./scripts/mutation-baseline.sh submission
bash ./scripts/mutation-baseline.sh github
bash ./scripts/mutation-baseline.sh jira
bash ./scripts/mutation-baseline.sh gateway
bash ./scripts/mutation-baseline.sh meeting
bash ./scripts/mutation-baseline.sh all
```

Each command verifies the corresponding normal unit/mock suite with database integration tests excluded, verifies the dedicated mutation harness, runs the selected Stryker subscopes and regenerates `artifacts/mutation/mutation-summary.md` when Python 3 is available.

New scopes intentionally use `break = 0` until a valid baseline and survivor-hardening pass are complete. Do not introduce arbitrary exclusions or thresholds merely to improve the reported percentage.
