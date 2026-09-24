# Azure Production Documentation Index

This directory is designed so an implementation agent does **not** need to reread one enormous plan for every task.

## First read

Always read:

1. `DECISIONS.md`
2. this `README.md`

Then open only the document relevant to the task.

## Documents

### `MASTER_PLAN.md`

Full architecture and implementation plan.

Use when:
- onboarding,
- reviewing the whole design,
- resolving a cross-cutting architecture question.

Do **not** reread this file for every small implementation task.

### `01_NETWORKING_AND_SECURITY.md`

Use for:
- VNet/subnet changes,
- Container Apps ingress,
- private DNS,
- NSG/firewall rules,
- Nginx public/private routing,
- TLS,
- public/private ports.

### `02_INFRASTRUCTURE_VM.md`

Use for:
- VM Bicep,
- disk mounting,
- Docker installation,
- Nginx,
- MySQL,
- Kafka,
- Prometheus,
- Grafana,
- persistent storage,
- host hardening.

### `03_IAC_AND_BOOTSTRAP.md`

Use for:
- OIDC,
- Azure RBAC,
- Bicep,
- resource naming,
- What-If,
- infrastructure workflow,
- Azure Run Command.

### `04_PRODUCTION_CICD.md`

Use for:
- `main` deployment,
- GHCR,
- changed-service detection,
- secrets/env delivery,
- DB migration,
- Container App revisions,
- rollback,
- deployment validation.

### `05_CUTOVER_AND_VALIDATION.md`

Use for:
- first live Azure deployment,
- DNS,
- Let's Encrypt,
- GitHub/Jira callbacks,
- migration verification,
- final smoke tests,
- rollback/cutover.

### `RUNBOOK.md`

Use for:
- operating the implemented workflows,
- GitHub Environment secrets/variables,
- first deployment, data migration, cutover commands.

### `../configuration/`

Use for:
- every GitHub Environment secret/variable (Test and Production), its source and consumer,
- filling service env bundles,
- external integrations (GitHub App, Jira, DNS),
- the first-deployment checklist.

### `IMPLEMENTATION_STATUS.md`

Update after every meaningful implementation session.

This prevents future agents from re-investigating already completed work.

## Critical repository rule

Never inspect real `.env` files.

Use only `.env.example` contracts.
