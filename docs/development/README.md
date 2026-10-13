# Developer guides

Use these guides from the backend repository root. The root README is the overview;
these documents contain the detailed workflows and reference material.

| Task | Guide |
| --- | --- |
| Set up and run the backend | [Local setup](local-setup.md) |
| Check onboarding completion | [First-run checklist](first-run-checklist.md) |
| Understand ENV loading and secrets | [Secrets and configuration](secrets.md) |
| Configure integrations and policies | [Service configuration](service-configuration.md) |
| Provision service databases | [Database setup](../database/setup.md) |
| Manage schemas and generate SQL | [Migrations](../database/migrations.md) |
| Look up commands and options | [Command reference](commands.md) |
| Read service API schemas | [Swagger/OpenAPI](../api/openapi.md) |
| Verify tests and quality | [Test strategy](../testing/test-strategy.md) |
| Diagnose a failure | [Troubleshooting](troubleshooting.md) |

## Previous README content map

The previous long README mixed operating guidance, feature notes and early-stage
claims. Its useful material is organized as follows; obsolete claims are replaced
by current behavior rather than retained as setup instructions.

| Previous topic | Current destination |
| --- | --- |
| Technology baseline, architecture and service ports | Root README and architecture overview/repository map. |
| Git strategy and CI/CD | Root README, CONTRIBUTING and existing deployment/branching guides. |
| Configuration model and service ownership | Secrets and configuration; service configuration. |
| Prerequisites and first-time setup (sections 1–2) | Local setup and first-run checklist. |
| Service-specific settings (section 3) | Service configuration, canonical templates and provider guides. |
| Administrator configuration, provisioning and DB status (sections 4–6) | Database setup. |
| EF Core operations (section 7) | Migrations, including SQL generation and pending-state behavior. |
| Startup profiles, health and logs (sections 8–9) | Local setup and command reference. |
| Swagger (section 10) | Swagger/OpenAPI guide, including all six gateway document routes. |
| Registration policy, validation, aliases, roles, persistence and acceptance mapping (section 11) | Registration guide; current OTP completion and configurable role selection replace blanket old claims. |
| Login, sessions, roles and schema notes (section 12) | Existing US-102 guide, with current resource access guidance. |
| API envelopes and errors (section 13) | API conventions, including JSON examples. |
| Tests, integration coverage and pre-PR checks (sections 14–15) | Test strategy and existing testing/quality/coverage guides. |
| Environment syntax, secret handling and User Secrets rationale (sections 16–17) | Secrets and configuration. |
| Common commands and seeding (section 18) | Command reference and local setup; seeder limitation explicitly documented. |
| Diagnostic cases (section 19) | Troubleshooting. |
| Code/ENV boundaries and shared technical helpers (section 20) | Secrets/configuration and architecture guides. |
| Deployment, performance and assessment coverage appendices | Root navigation and existing deployment/performance/testing guides. |

The committed and staged README shared these substantive topics; the staged
Swagger additions are preserved in the OpenAPI guide and root navigation. The
shortened working README's setup/testing/deployment guidance is retained in the
corresponding guides rather than used to replace the detailed material.
