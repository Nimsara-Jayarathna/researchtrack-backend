# Repository map and ownership

## `src/Gateway/ResearchTrack.Gateway`

Public entry point for the React frontend. Owns YARP routing, frontend CORS, gateway-level rate limiting, shared API middleware, and gateway health endpoints. It must not contain ResearchTrack business logic.

## `src/BuildingBlocks/ResearchTrack.BuildingBlocks.Api`

Small shared technical library containing API envelopes, generic error codes, exception/status-code handling, correlation IDs, request logging, security headers, health response formatting, and OpenAPI registration. Do not add domain entities or feature workflows here.

## `src/Services/ResearchTrack.AuthService`

Owns authentication/user capability. Sprint 1 feature developers add registration, institutional identity/role rules, login/session behavior, and related persistence here.

## `src/Services/ResearchTrack.ProjectService`

Owns Research Project and membership capability. Sprint 1 feature developers add project CRUD/management, membership, and project access rules here.

## `src/Services/ResearchTrack.GitHubService`

Owns GitHub connection, repository metadata, default-branch activity, contributors, pull requests, and synchronization beginning in Sprint 2.

## `src/Services/ResearchTrack.JiraService`

Owns Jira OAuth/connection, issues, sprint/workload data, and synchronization beginning in Sprint 3.

## `src/Services/ResearchTrack.MeetingService`

Owns meeting channels, records, approval, and meeting history beginning in Sprint 4.

## `src/Services/ResearchTrack.SubmissionService`

Owns submission requirements, document submission metadata, review/feedback, revision/version workflow, and later the storage integration introduced with that feature in Sprint 4.

## `tests/ResearchTrack.Testing`

Shared test-only infrastructure. It may contain `WebApplicationFactory` and safe test configuration helpers, but no production business logic.

## `tools/ResearchTrack.DevSeeder`

Development-only seeding host. It remains intentionally feature-empty until real domain rules exist; later seeders must reuse the application rules rather than duplicate them.

## `scripts`

Developer convenience and safety layer for setup, local MySQL, EF migrations, running services, health checks, tests, quality checks, and service-owned environment configuration.

## `docs`

Architecture decisions and operational conventions. Documentation should track meaningful architecture/process changes through the project.
