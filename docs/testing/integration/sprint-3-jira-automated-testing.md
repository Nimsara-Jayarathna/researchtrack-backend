# Sprint 3 Jira automated testing

This suite expands `ResearchTrack.JiraService.Tests` around the Jira risks exercised by Sprint 3 QA.

## Recommended branch

Create the work from `develop` using the repository convention:

```text
feature/RT-XX-jira-qa-automated-tests
```

Replace `RT-XX` with the real Jira ticket. If the work is tied specifically to a defect fix, use:

```text
bugfix/RT-XX-jira-sync-test-coverage
```

## Coverage added

- Jira issue field mapping, defaults, optional/null fields, people, time tracking, and story points
- Issue query summaries and Jira health calculations
- Sprint progress counts, percentages, story points, zero-issue sprints, and no-active-sprint behavior
- Workload grouping, unassigned work, story points, and completed work
- OAuth credential protection using stable environment keys
- Tamper/wrong-key/lost-legacy-key handling
- Atlassian refresh-token rotation and refresh failure behavior
- Sync-state transitions (`SYNCING`, `SYNCED`, `FAILED`, `INVALID_AUTH`) while preserving the last successful snapshot timestamp
- Webhook bearer validation, project routing, duplicate delivery handling, unmatched-event persistence, and sync scheduling
- Access/manage authorization hooks and controller authorization policies
- Sync-state query behavior

## Run

```bash
./scripts/test.sh jira
```

For full repository verification:

```bash
./scripts/check.sh
```

For database integration tests when a test MySQL environment is configured:

```bash
./scripts/test.sh integration
```

The Jira test project already uses `coverlet.collector`; Jira test runs therefore emit Cobertura coverage under `TestResults/`.
`TestResults/` and generated coverage HTML must not be committed.
