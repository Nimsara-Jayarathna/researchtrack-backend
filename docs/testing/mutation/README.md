# Mutation testing

ResearchTrack uses repository-local Stryker.NET tooling and dedicated xUnit v2 mutation harnesses to assess selected backend business logic. The normal unit/mock suites use xUnit v3 and remain authoritative for regular CI and coverage reporting.

## Start here

- [Runbook](runbook.md): prerequisites, service commands, reports, and troubleshooting.
- [Strategy](strategy.md): scope boundaries, harness design, and survivor review.
- [Baseline and regression policy](baseline.md): recorded results, score calculation, and gate status.
- [Implementation history](history/implementation.md): initial setup, xUnit compatibility investigation, and tool-manifest correction.

## Service documentation

| Service | Documentation |
|---|---|
| AuthService | [Scope](services/auth/scope.md), [hardening history](services/auth/hardening.md) |
| ProjectService | [Hardening history](services/project/hardening.md) |
| SubmissionService | [Hardening history](services/submission/hardening.md) |
| GitHubService | [Hardening history and reconciliation refinement](services/github/hardening.md) |
| JiraService | [Hardening history and webhook corrections](services/jira/hardening.md) |
| Gateway | [Scope](services/gateway/scope.md) |
| MeetingService | [Hardening history](services/meeting/hardening.md) |

## Documentation conventions

Use lowercase, hyphen-separated names for Markdown files and folders; reserve `README.md` for the entry point. Keep shared operating instructions at this level, service-specific material under `services/<service>/`, and superseded setup decisions under `history/`.

Maintain one hardening record per service. Add subsequent passes as clearly labeled sections, preserve the baseline that motivated each pass, and distinguish historical measurements from current regression settings. Keep commands in the runbook and shared scoring/policy rules in the baseline document. Use relative Markdown links for navigation. Run commands from the repository root.
