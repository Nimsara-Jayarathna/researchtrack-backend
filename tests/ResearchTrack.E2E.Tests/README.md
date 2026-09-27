# ResearchTrack Selenium E2E Tests

This project contains the Sprint 3 browser E2E suite for stable ResearchTrack-owned workflows. Jira/Atlassian OAuth connect/disconnect is intentionally excluded because it depends on external authentication.

## Scenarios

- E2E-001: Issues -> Issue Details -> Sprint -> Workload
- E2E-002: Prepared Jira change -> Refresh -> Updated UI
- E2E-003: Student Jira permission behavior
- E2E-004: Project navigation consistency

## Setup

1. Install Google Chrome.
2. Copy `.env.e2e.example` to `.env.e2e` at the repository root.
3. Fill in Test-environment supervisor/student credentials and a project ID accessible to both roles.
4. For E2E-002, modify the configured Jira issue before the run and set the expected issue key/status values.

Selenium Manager resolves the compatible Chrome driver automatically through `Selenium.WebDriver`.

## Run

```bash
./scripts/test.sh e2e
```

Failure screenshots and diagnostic text are written under `TestResults/E2E/`.
