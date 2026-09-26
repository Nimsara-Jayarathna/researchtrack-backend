# Test Deployment (`develop` → VPS + Docker Compose)

The Test path is **unchanged** by the Azure Production work and must stay that way (see the root `CLAUDE.md`, section 5).

```text
push to develop ─▶ backend-deploy-test.yml ─▶ backend-deploy-reusable.yml (environment_name=test)
                                               ├─ backend-build-images.yml  → ghcr.io/<owner>/researchtrack-<svc>:test and :<sha>
                                               ├─ validate secrets/variables, materialize env files
                                               ├─ deploy/validate-env-files.sh <dir> config/env test
                                               ├─ scp compose.yml, monitoring, mysql, scripts, env files → <BACKEND_DEPLOY_ROOT>/test
                                               ├─ deploy/scripts/deploy-stack.sh   (MySQL, DB users, dbcheck, migrate, services)
                                               └─ deploy/scripts/verify-health.sh
```

Runtime: one Docker Compose project, `researchtrack-test` (`deploy/compose.yml`). It runs the Gateway, the six services, MySQL 8.4, Prometheus and Grafana on the VPS. Only the Gateway joins the external reverse-proxy network (`NPM_NET_NAME`). Grafana is bound to `127.0.0.1:<GRAFANA_HOST_PORT>` and reached through an SSH tunnel.

## GitHub Environment `test`

| Name | Type | Required | Notes |
|---|---|---|---|
| `SSH_HOST`, `SSH_PORT`, `SSH_USER`, `SSH_PRIVATE_KEY` | Secret (Environment or repository) | yes | `SSH_PORT` must be numeric |
| `MYSQL_ENV_FILE`, `SHARED_AUTH_ENV_FILE`, `GATEWAY_ENV_FILE`, `AUTH_ENV_FILE`, `PROJECT_ENV_FILE`, `RT_GITHUB_SERVICE_ENV`, `JIRA_ENV_FILE`, `MEETING_ENV_FILE`, `SUBMISSION_ENV_FILE`, `GRAFANA_ENV_FILE` | Secret (multiline) | yes | Uploaded to the VPS as `env/<name>.env`, mode `0600` |
| `BACKEND_DEPLOY_ROOT` | Variable | yes | Absolute path, e.g. `/opt/researchtrack/backend` |
| `NPM_NET_NAME` | Variable | no | Default `npm_default` |
| `GRAFANA_HOST_PORT` | Variable | no | Default `9000`, loopback only |
| `GRAFANA_MEMORY_LIMIT` | Variable | no | Default `512m` |

The workflow writes a non-secret `deploy.env` on the VPS from these values: `DEPLOY_ENV=test`, `IMAGE_PREFIX=ghcr.io/<owner>`, `EDGE_NETWORK`, `GRAFANA_HOST_PORT` and `GRAFANA_MEMORY_LIMIT`. The VPS pulls images with the run's temporary `GITHUB_TOKEN`; no registry secret is needed.

## Test rules for the env bundles (`deploy/validate-env-files.sh`)

| Rule | Files |
|---|---|
| Every contract key present, no duplicate keys | all |
| `ASPNETCORE_ENVIRONMENT=Test`, `DOTNET_ENVIRONMENT=Test`, `ASPNETCORE_URLS=http://+:8080` | 7 service files |
| Gateway URLs exactly `http://auth:8080`, `http://project:8080`, … (Compose service discovery) | gateway |
| `Services__Auth__BaseUrl=http://auth:8080`; `Services__Project__BaseUrl=http://project:8080` | project, github |
| Connection string `Server=mysql`, `Port=3306`, `SslMode=Disabled`, database/user/password equal to `mysql.env` | 6 DB services |
| Non-empty, non-placeholder: `MYSQL_ROOT_PASSWORD`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` (≥32 chars), `FRONTEND_ORIGIN`, `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays`, `Brevo__ApiKey`, `GF_SECURITY_ADMIN_PASSWORD` | as named |
| `GitHub__SyncIntervalMinutes` between 1 and 1440 | github |

Unlike Production, Test does not reject other placeholder values, does not require HTTPS URLs, and keeps MySQL TLS disabled inside the internal Docker network. `GF_SERVER_ROOT_URL` should match the SSH-tunnel URL, e.g. `http://localhost:<GRAFANA_HOST_PORT>`.

The same secret names exist in `production` with **different** values and the Azure-specific rules in [PRODUCTION_AZURE.md](PRODUCTION_AZURE.md).
