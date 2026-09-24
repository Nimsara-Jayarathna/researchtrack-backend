# Production CI/CD Plan

## Test

`develop` remains unchanged.

## Production

`main` deploys Azure Production.

## Images

Keep GHCR.

Use immutable SHA/version tags.

## Azure login

Use OIDC.

Required permission:

```text
id-token: write
contents: read
```

No stored Azure password.

## Preflight

Before app deployment check:

- Resource Group,
- VM,
- Container Apps environment,
- expected applications,
- private network readiness.

Fail clearly if infrastructure is missing.

## Configuration

Read variable names from `.env.example` contracts.

Real production values come from GitHub Environment `production`.

Do not read or commit real `.env` files.

## Deployment

For changed services:

```text
build
→ GHCR
→ dbcheck
→ migration
→ new Container App revision
→ readiness
→ activate
```

Unchanged services are not redeployed.

## Rollback

Keep previous healthy revision until new revision passes health checks.

If new revision fails:
- disable it,
- retain previous revision,
- fail the workflow.

## VM updates

Use Azure Run Command.

Generate temporary runtime configuration from GitHub production secrets.

Do not expose secrets in logs.

## Final verification

Check:

- all seven services,
- public Nginx → Gateway,
- MySQL connectivity,
- Kafka produce/consume,
- Prometheus target state/rules,
- Grafana datasource/dashboards.

## Replicas

Start:

```text
min=1
max=1
```

Do not scale until statelessness/background jobs are reviewed.
