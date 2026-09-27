# ResearchTrack JiraService automated tests

The Jira service test suite is organized around the Sprint 3 QA risks and the production Jira architecture.

## Covered areas

- Jira issue query/mapping and status summaries
- Sprint progress calculations, including zero-issue and no-active-sprint cases
- Assignee workload calculations and unassigned issues
- OAuth token protection, stable environment keys, legacy migration, tamper detection, and container-replacement behavior
- Atlassian refresh-token rotation behavior
- Webhook authentication, routing, duplicate delivery handling, and coalesced sync scheduling behavior
- Project authorization hooks used by Jira query/refresh flows
- Jira sync-state reads
- Controller authorization policies

## Run Jira unit tests with coverage

```bash
./scripts/test.sh jira
```

Or directly:

```bash
dotnet test tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj \
  -c Release \
  --collect:"XPlat Code Coverage" \
  --results-directory TestResults/JiraService
```

Coverage output is written as `coverage.cobertura.xml` under `TestResults/JiraService/...`.
Do not commit `TestResults/` or generated HTML coverage reports.

## Full repository verification

```bash
./scripts/check.sh
```
