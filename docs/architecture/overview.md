# ResearchTrack backend architecture

ResearchTrack uses a backend monorepo containing an API Gateway and six independently runnable ASP.NET Core business services: Auth, Project, GitHub, Jira, Meeting, and Submission.

The monorepo is a source-management decision, not a shared runtime/data boundary. Every service has its own project, configuration, `DbContext`, migrations, database, tests, health endpoints, and deployment lifecycle.

Local services run with the .NET SDK and connect to a configured MySQL server. Test deployment uses VPS/Docker Compose; Production uses Azure Container Apps and an infrastructure VM with database and monitoring infrastructure. Deployment, Prometheus/Grafana configuration, S3-compatible submission storage, and provider synchronization are implemented. See the [deployment configuration guide](../devops/configuration/README.md) and [local setup](../development/local-setup.md).

The frontend communicates through the YARP Gateway. Services must not directly read/write another service's database. `ResearchTrack.BuildingBlocks.Api` contains only cross-cutting technical conventions such as API envelopes, exception handling, tracing, logging, security headers, health response formatting, and OpenAPI registration.
