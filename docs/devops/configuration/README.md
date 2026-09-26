# Deployment Configuration Guide

This guide covers every GitHub Environment secret and variable, every deployment configuration value, and every operator setup step for **Test** (`develop` → VPS + Docker Compose) and **Production** (`main` → Azure).

It is derived from the implementation: `.github/workflows/`, `deploy/`, `deploy/azure/` and `config/env/*/.env.example`. If this guide and the code ever disagree, the code is the truth; fix the guide.

Architecture and design reasoning are **not** repeated here; see [`../azure-production/`](../azure-production/README.md).

| I want to… | Read |
|---|---|
| See every secret and variable, its type, where it comes from and who consumes it | [GITHUB_ENVIRONMENTS.md](GITHUB_ENVIRONMENTS.md) |
| Configure the GitHub `production` Environment for Azure | [PRODUCTION_AZURE.md](PRODUCTION_AZURE.md) |
| Understand or maintain the Test (`develop`) VPS deployment | [TEST_VPS.md](TEST_VPS.md) |
| Fill in a service env bundle (`AUTH_ENV_FILE`, `MYSQL_ENV_FILE`, …) | [SERVICE_CONFIGURATION.md](SERVICE_CONFIGURATION.md) |
| Configure the GitHub App, Jira OAuth, DNS or the frontend origin | [EXTERNAL_INTEGRATIONS.md](EXTERNAL_INTEGRATIONS.md) |
| Bring Production up for the first time | [FIRST_DEPLOYMENT_CHECKLIST.md](FIRST_DEPLOYMENT_CHECKLIST.md) |

Related:
- [`config/env/README.md`](../../../config/env/README.md): the configuration-contract model (`.env.example` files) and local development.
- [`../azure-production/RUNBOOK.md`](../azure-production/RUNBOOK.md): Production operations (logs, rollback, backups, data migration commands).
- [`../azure-production/IMPLEMENTATION_STATUS.md`](../azure-production/IMPLEMENTATION_STATUS.md): what has and has not been proven on real Azure.

## Ground rules

- **Real values are never committed.** Only `config/env/*/.env.example`, which contains names and placeholders, lives in Git.
- Real Test values live in the GitHub Environment `test`, and real Production values in `production`. There is no second secret store; Azure Container App secrets are populated from GitHub on every deployment.
- Test and Production use **different** credentials, especially the JWT signing key, database passwords and the Grafana admin password.
- Tools, scripts and AI agents must never read real env files (see the root `CLAUDE.md`).
