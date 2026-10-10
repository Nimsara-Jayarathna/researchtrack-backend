# Final mutation-testing baseline and regression policy

## Status

The survivor-hardening phase is complete for the selected ResearchTrack backend business-logic portfolio. The service-level baselines below are now treated as stable evidence, not as targets that must be pushed toward 100%.

| Service | Hardened weighted baseline | Regression floor | Status |
|---|---:|---:|---|
| AuthService | 74.34% | 70% | Locked |
| ProjectService | 77.05% | 72% | Locked |
| SubmissionService | 63.26% | 58% | Locked |
| GitHubService | 65.48% | 60% | Locked |
| JiraService | 67.89% | 62% | Locked |
| Gateway | 78.08% | 72% | Locked |
| MeetingService | 74.00% | 68% | Locked |
| Project-wide selected business logic | calculated by final `all` run | 65% | Enforced on full run |

The authoritative project-wide score is generated from actual mutant counts by `scripts/mutation-summary.py`; service percentages are never averaged.

## Why the floors are below the baselines

Mutation scores can contain equivalent mutants and low-value transformations, and Stryker may classify some generated mutants as compile errors. The regression floors are therefore deliberately below the hardened baselines. Their purpose is to catch a meaningful loss of test effectiveness while avoiding brittle CI that rewards artificial exclusions or endless score chasing.

Two levels of protection are used:

1. **Per-subscope Stryker `break` thresholds** in each version-controlled Stryker config. These fail the mutation run immediately if a specific business area drops substantially below its hardened baseline.
2. **Weighted service/project gates** in `config/testing/mutation-policy.json`. These are enforced by the final `all` run after the Markdown/JSON/HTML reports are generated.

## Final evidence command

Run the full portfolio once from a clean branch state:

```bash
bash ./scripts/mutation-baseline.sh all
```

The command verifies every normal unit/mock suite with database integration excluded, verifies every dedicated mutation harness, runs all selected Stryker scopes, regenerates `artifacts/mutation/mutation-summary.md`, and enforces the weighted regression policy.

A successful final run must satisfy all of the following:

- every normal unit/mock suite passes;
- every dedicated mutation harness passes;
- every Stryker subscope satisfies its configured `break` floor;
- all service reports are present;
- all weighted service floors pass;
- the overall selected-business-logic score is at least 65%;
- `Timeout` and `No coverage` are reviewed explicitly rather than hidden;
- HTML, JSON, Markdown and the project-wide summary are retained as evidence.

## Reporting rule

The final report must show `Killed`, `Survived`, `Timeout`, `No coverage`, `Ignored`, and `Compile error` separately. `Ignored` and `Compile error` are not counted as surviving business mutants. Weighted scores use actual mutant counts:

```text
mutation score = (Killed + Timeout) /
                 (Killed + Timeout + Survived + No coverage)
```

Do not average subscope percentages.

## Final assessment narrative

The project started from narrow/high-scoring deterministic rule scopes and then expanded mutation testing into real orchestration, authorization, workflow, webhook, synchronization, and state-transition logic. Survivor review drove targeted tests in weak areas rather than broad exclusions. Representative improvements include ProjectService orchestration, Submission workflow, Jira webhook handling, GitHub synchronization decisions, and Auth authentication/registration behavior. The final thresholds preserve those gains as regression gates.


## Final runner interface

The production-ready runner deliberately exposes only stable service-level scopes:

```text
all
auth
project
submission
github
jira
gateway
meeting
```

The final testing contract exposes service-level scopes only. Each service command verifies its normal unit/mock suite, verifies its dedicated mutation harness, runs every finalized mutation subscope for that service, and regenerates the common report.
