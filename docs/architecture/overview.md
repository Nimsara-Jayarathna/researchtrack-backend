# ResearchTrack backend architecture

ResearchTrack uses a backend monorepo containing an API Gateway and six independently runnable ASP.NET Core business services: Auth, Project, GitHub, Jira, Meeting, and Submission.

The monorepo is a source-management decision, not a shared runtime/data boundary. Every service has its own project, configuration, `DbContext`, migrations, database, tests, health endpoints, and future deployment lifecycle.

At the current development-foundation stage, services run directly with the .NET SDK and connect to a developer-installed MySQL server. Docker, cloud/container deployment, and Prometheus/Grafana deployment configuration are deferred DevOps work.

The frontend communicates through the YARP Gateway. Services must not directly read/write another service's database. `ResearchTrack.BuildingBlocks.Api` contains only cross-cutting technical conventions such as API envelopes, exception handling, tracing, logging, security headers, health response formatting, and OpenAPI registration.
