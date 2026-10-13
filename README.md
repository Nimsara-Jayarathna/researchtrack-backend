# ResearchTrack Backend

ResearchTrack supports student research projects and supervisor review through six
ASP.NET Core microservices and a YARP API gateway. It includes authentication,
project membership and dashboards, GitHub and Jira integrations, supervision
meetings, and submission review with version history and S3-compatible storage.

The repository contains application code, database migrations, automated tests,
and deployment infrastructure. Test deployments use a VPS with Docker Compose;
Production uses Azure Container Apps and an infrastructure VM.

## Start here

- [Local setup](#local-quick-start)
- [Swagger and OpenAPI](#swagger-and-openapi)
- [Tests and code quality](#tests-and-code-quality)
- [Deployment and operations](#deployment-and-operations)
- [Documentation index](docs/README.md)
- [Contributing](CONTRIBUTING.md) · [Security policy](SECURITY.md)

## Architecture

The frontend sends HTTP requests to the gateway. YARP forwards business requests
to the owning service; each service owns its database, EF Core migrations,
configuration, and tests. Services do not read or write another service's database.

| Component | Local port | Responsibilities |
| --- | ---: | --- |
| Gateway | 5000 | API routing, CORS, rate limiting, forwarded headers, service OpenAPI access |
| Auth | 5101 | Registration, email verification, login, refresh sessions, password reset |
| Project | 5102 | Projects, memberships, supervisor and student dashboards |
| GitHub | 5103 | GitHub App authorization, repository linking, webhooks, synchronized activity |
| Jira | 5104 | Atlassian OAuth, project selection, webhooks, synchronized progress |
| Meeting | 5105 | Meeting records, participants, approval workflows |
| Submission | 5106 | Uploads, versions, review, feedback, resubmissions |

Local frontend requests use `http://localhost:5000`; the frontend normally runs
on port 5173. Containerized services use port 8080 instead of these local ports.

GitHub and Jira use durable webhook processing and synchronization workers.
Optional Kafka transports use transactional outboxes and durable receipts;
Kafka is disabled by default and is not a prerequisite for ordinary local startup.
See [Kafka contracts and flows](docs/devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md)
for enablement requirements and recovery behavior.

### Repository layout

```text
src/Gateway/         YARP gateway
src/Services/        Auth, Project, GitHub, Jira, Meeting, Submission
src/BuildingBlocks/  Shared technical infrastructure
config/env/          Canonical per-component environment templates
scripts/             Local development, database, quality and test commands
tests/              Service tests and performance tooling
deploy/             Container images, Compose, Azure and monitoring infrastructure
docs/               Architecture, API, feature, testing and operations guides
```

Shared building blocks provide technical conventions, including API envelopes,
error handling, configuration, logging, health checks and OpenAPI. Domain logic
and service-owned data stay in their service projects.

## Local quick start

Use the .NET SDK selected by `global.json` (10.0.300 or a compatible later .NET 10
feature band), Git, Bash 4+, `curl`, and a reachable MySQL 8+ server/client.
On macOS use an updated Bash; on Windows WSL is recommended.

1. Run `./scripts/setup.sh` to restore tools/packages and create missing local files.
2. Configure `config/env/shared/.env.local` and the selected service `.env.local`
   files using their committed examples. Setup does not fill required values.
3. Have the database administrator provision scoped development/test databases if
   needed; follow [database setup](docs/database/setup.md).
4. Apply migrations and start a profile:

```bash
./scripts/migrate.sh auth
./scripts/migrate.sh project
./scripts/dev.sh core
./scripts/health.sh core
```

`core` runs Auth, Project and Gateway. Other profiles are `integrations`, `research`
and `all`. For a fully configured/provisioned backend, `./scripts/start-all.sh`
checks databases, builds, migrates, starts all services and waits for readiness.
Stop the chosen profile with `./scripts/stop.sh core`; background logs live under
`.run/logs/`. See the [complete setup/run guide](docs/development/local-setup.md)
for prerequisites, options, health semantics and troubleshooting.

## Swagger and OpenAPI

Open **http://localhost:5000/swagger** locally. After deploying the service Swagger
routes, Production documentation is at
**https://api.researchtrack.blipzo.xyz/swagger**. The UI selects independent Auth,
Project, GitHub, Jira, Meeting, Submission and gateway documents.

Service JSON paths use `/swagger/{service}/v1/swagger.json`; the gateway's original
path remains `/swagger/v1/swagger.json`. Empty gateway `paths` is expected when it
has no local controllers. Set `OpenApi__Enabled=true` on the gateway and each
service outside Development, then deploy/restart affected components. See the
[OpenAPI guide](docs/api/openapi.md) for all URLs, enablement and diagnostics.

## Tests and code quality

```bash
./scripts/test.sh all
# Explicitly exclude external database and Kafka integration categories:
dotnet test ResearchTrack.sln --filter 'Category!=DatabaseIntegration&Category!=KafkaIntegration'
```

Database integration tests, coverage, mutation, CSharpier/Roslyn quality checks,
SonarQube and authenticated k6 testing have separate prerequisites. Use the
[test strategy](docs/testing/test-strategy.md) and [testing index](docs/testing/README.md)
for exact commands and evidence locations.

## Deployment and operations

| Environment | Branch | Runtime |
| --- | --- | --- |
| Test | `develop` | VPS with Docker Compose |
| Production | `main` | Azure Container Apps and infrastructure VM |

GitHub Actions implements image builds, deployment, configuration/infrastructure
validation and quality workflows. Production exposes the gateway through Nginx;
business services remain private. Database and Prometheus/Grafana infrastructure
are implemented. Production may be intentionally stopped for cost control;
runtime sessions/performance automation preserve and restore its prior state.
See [deployment configuration](docs/devops/configuration/README.md), the
[Azure runbook](docs/devops/azure-production/RUNBOOK.md) and
[runtime operations](docs/devops/azure-runtime/README.md).

## Detailed guides

| Task | Reference |
| --- | --- |
| Set up, run, inspect logs and stop | [Local development](docs/development/local-setup.md) |
| Confirm onboarding is complete | [First-run checklist](docs/development/first-run-checklist.md) |
| Configure ENV, shared JWT and secrets | [Configuration rules](docs/development/secrets.md) |
| Configure providers and service policies | [Service configuration](docs/development/service-configuration.md) |
| Provision databases and verify scoped access | [Database setup](docs/database/setup.md) |
| Apply/add migrations or generate SQL | [EF Core migrations](docs/database/migrations.md) |
| Look up script syntax and options | [Command reference](docs/development/commands.md) |
| Understand API envelopes and access rules | [API conventions](docs/api/conventions.md) |
| Configure Swagger and inspect all service APIs | [OpenAPI guide](docs/api/openapi.md) |
| Understand registration and email verification | [Registration guide](docs/stories/registration.md) |
| Understand login, cookies and authorization | [Session lifecycle](docs/development/us-102-secure-login-rbac.md) |
| Run tests, integration checks and coverage | [Test strategy](docs/testing/test-strategy.md) |
| Run production C# quality checks | [Code quality](docs/testing/code-quality/README.md) |
| Run authenticated performance scenarios | [k6 guide](tests/performance/README.md) |
| Diagnose startup, database, routing and Swagger failures | [Troubleshooting](docs/development/troubleshooting.md) |
| Find all feature/operations guides | [Documentation index](docs/README.md) |
| Locate material from the previous long README | [Content map](docs/development/README.md#previous-readme-content-map) |

Keep real environment files, signing keys, provider credentials and storage secrets
out of Git. See [CONTRIBUTING](CONTRIBUTING.md) for collaboration rules and
[SECURITY](SECURITY.md) for vulnerability reporting.
