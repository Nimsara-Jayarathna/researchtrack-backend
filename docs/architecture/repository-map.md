# Repository map and ownership

## `src/Gateway/ResearchTrack.Gateway`

Public entry point for the React frontend. Owns YARP routing, frontend CORS, gateway-level rate limiting, shared API middleware, and gateway health endpoints. It must not contain ResearchTrack business logic.

## `src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api`

Small shared technical library containing API envelopes, generic error codes, exception/status-code handling, correlation IDs, request logging, security headers, health response formatting, and OpenAPI registration. Do not add domain entities or feature workflows here.

## `src/Services/ResearchTrack.AuthService`

Owns registration/email verification, configurable institutional identity/role rules, login/session lifecycle, password reset/change and related persistence.

## `src/Services/ResearchTrack.ProjectService`

Owns project creation/management, membership, accessible project reads, dashboards, and resource authorization.

## `src/Services/ResearchTrack.GitHubService`

Owns GitHub connection, repository metadata, default-branch activity, contributors, pull requests, and synchronization.

## `src/Services/ResearchTrack.JiraService`

Owns Jira OAuth/connection, issues, sprint/workload data, and synchronization.

## `src/Services/ResearchTrack.MeetingService`

Owns meeting channels, records, approval, and meeting history.

## `src/Services/ResearchTrack.SubmissionService`

Owns submission requirements, document submission metadata, review/feedback, revision/version workflow, and S3-compatible object-storage integration.

## `tests/ResearchTrack.Testing`

Shared test-only infrastructure. It may contain `WebApplicationFactory` and safe test configuration helpers, but no production business logic.

## `tools/ResearchTrack.DevSeeder`

Development-only seeding host. No service seeder is currently registered; invocation with a service exits with code 2. Future seeders must reuse application rules rather than duplicate them.

## `scripts`

Developer convenience and safety layer for setup, local MySQL, EF migrations, running services, health checks, tests, quality checks, and service-owned environment configuration.

## `docs`

Architecture decisions and operational conventions. Documentation should track meaningful architecture/process changes through the project.
