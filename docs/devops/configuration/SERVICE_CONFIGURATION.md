# Service Configuration Bundles

Each service's keys are defined **only** by its committed contract, `config/env/<service>/.env.example`. This page maps each contract to its GitHub secret and runtime destination, and shows where Test and Production values differ. Key names below come from the contracts; values are never shown.

Filling a bundle: copy the contract, keep **every** key (both validators fail on a missing key), replace the values, and paste the result as the multiline GitHub secret. Never commit the completed file.

## Mapping

| Contract | GitHub secret | Test runtime | Production runtime |
|---|---|---|---|
| `mysql` | `MYSQL_ENV_FILE` | `mysql` Compose service + `reconcile-databases.sh` | VM MySQL container, `reconcile-databases.sh`, nightly backup |
| `shared` | `SHARED_AUTH_ENV_FILE` | `auth`, `project`, `github` | `rt-auth-prod`, `rt-project-prod`, `rt-github-prod` (+ their migration jobs) |
| `gateway` | `GATEWAY_ENV_FILE` | `gateway` | `rt-gateway-prod` |
| `auth` | `AUTH_ENV_FILE` | `auth` | `rt-auth-prod`, `rt-migrate-auth-prod` |
| `project` | `PROJECT_ENV_FILE` | `project` | `rt-project-prod`, `rt-migrate-project-prod` |
| `github` | `RT_GITHUB_SERVICE_ENV` | `github` | `rt-github-prod`, `rt-migrate-github-prod` |
| `jira` | `JIRA_ENV_FILE` | `jira` | `rt-jira-prod`, `rt-migrate-jira-prod` |
| `meeting` | `MEETING_ENV_FILE` | `meeting` | `rt-meeting-prod`, `rt-migrate-meeting-prod` |
| `submission` | `SUBMISSION_ENV_FILE` | `submission` | `rt-submission-prod`, `rt-migrate-submission-prod` |
| `grafana` | `GRAFANA_ENV_FILE` | `grafana` Compose service | VM Grafana container |
| `admin` | *(none)* | — | — (local `scripts/db-init.sh` only) |

In Production, `deploy/azure/scripts/render-containerapp.py` turns each bundle into Container App configuration:
- Keys matching `ConnectionStrings__*`, `*Password`, `*Secret`, `*SecretKey`, `*SigningKey`, `*ApiKey`, `*AccessKey`, `*PrivateKey`/`*PrivateKeyBase64` become **Container App secrets** (passed as a `@secure()` Bicep parameter).
- All other keys become plain environment variables.
- A `RESEARCHTRACK_DEPLOYMENT_REVISION` hash is added automatically.

## Keys that differ by environment

Key names are identical everywhere. Only these **values** follow an environment rule. All other keys hold ordinary per-environment business or credential values.

| Key | Test | Production |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT` | `Test` | `Production` |
| `ASPNETCORE_URLS` | `http://+:8080` | `http://+:8080` |
| `ConnectionStrings__DefaultConnection` | `Server=mysql;Port=3306;…;SslMode=Disabled` | `Server=10.20.10.4;Port=3306;…;SslMode=Required` |
| `Database__Host` / `Database__SslMode` (fallback only) | `mysql` / `Disabled` | `10.20.10.4` / `Required` |
| `*_SERVICE_URL` (gateway) | `http://<svc>:8080` | `http://rt-<svc>-prod` |
| `Services__Auth__BaseUrl` / `Services__Project__BaseUrl` | `http://auth:8080` / `http://project:8080` | `http://rt-auth-prod/` / `http://rt-project-prod/` |
| Public URLs (frontend origin, reset link, GitHub/Jira callbacks) | Test hosts | Production HTTPS hosts; must not contain `TEST_HOSTNAMES` |
| `Cookie__Secure` | per Test setup | `true` |
| `GF_SERVER_ROOT_URL` | `http://localhost:<GRAFANA_HOST_PORT>` (SSH tunnel) | `https://grafana.researchtrack.blipzo.xyz` |
| Credentials: JWT signing key, DB passwords, Grafana password, OAuth/App secrets | Test values | **Different** Production values |

## Per-service keys

**Common to all seven services:** `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `ASPNETCORE_URLS`, `OpenApi__Enabled`. Swagger is served when this is `true` or when running in Development; in Production, `false` is recommended.

**Common to the six DB services** (auth, project, github, jira, meeting, submission):
- `ConnectionStrings__DefaultConnection` (secret). This is what the services and the `dbcheck`/`migrate` tools use.
- `Database__Host`, `Database__Port`, `Database__Name`, `Database__TestName`, `Database__Username`, `Database__Password` (secret), `Database__SslMode`, `Database__AllowPublicKeyRetrieval`. These are the fallback when no connection string is set, and are also used by local scripts.

| Service | Service-specific keys | Secret-like | Source of values |
|---|---|---|---|
| **mysql** | `MYSQL_ROOT_PASSWORD`; `<SVC>_DB_NAME`, `<SVC>_DB_USER`, `<SVC>_DB_PASSWORD` for AUTH, PROJECT, GITHUB, JIRA, MEETING, SUBMISSION | root and all `*_DB_PASSWORD` | Generated. Names/users usually keep the contract defaults (`researchtrack_<svc>`, `rt_<svc>`). Each service connection string must repeat its database/user/password exactly |
| **shared** | `Jwt__Issuer`, `Jwt__Audience`, `Jwt__SigningKey` | `Jwt__SigningKey` (≥32 chars) | Generated; unique per environment |
| **gateway** | `FRONTEND_ORIGIN`, `AUTH_SERVICE_URL`, `PROJECT_SERVICE_URL`, `GITHUB_SERVICE_URL`, `JIRA_SERVICE_URL`, `MEETING_SERVICE_URL`, `SUBMISSION_SERVICE_URL` | — | Frontend URL (CORS) and internal addresses; mapped to YARP in `Program.cs` |
| **auth** | `Registration__*` (13 policy keys), `PasswordPolicy__*` (6), `PasswordHashing__*` (3), `PasswordReset__TokenExpiryMinutes`, `PasswordReset__FrontendBaseUrl`, `Brevo__BaseUrl`, `Brevo__ApiKey`, `Brevo__SenderEmail`, `Brevo__SenderName`, `Jwt__AccessTokenMinutes`, `Jwt__RefreshTokenDays`, `Cookie__Secure` | `Brevo__ApiKey` | Institutional policy (operator), Brevo account (external) |
| **project** | `Services__Auth__BaseUrl` | — | Internal address |
| **github** | `GitHub__AppId`, `GitHub__AppSlug`, `GitHub__ClientId`, `GitHub__ClientSecret`, `GitHub__PrivateKeyBase64`, `GitHub__WebhookSecret`, `GitHub__SetupCallbackUrl`, `GitHub__FrontendReturnOrigin`, `GitHub__StateExpiryMinutes`, `GitHub__AccessRequestExpiryHours`, `GitHub__RepositoryLinks__MaxLinkedRepositories`, `GitHub__RepositoryLinks__MaxEnabledRepositories`, `GitHub__SyncIntervalMinutes`, `GitHub__Webhook__MaxPayloadBytes`, `GitHub__Webhook__MaxAttempts`, `GitHub__Webhook__ProcessingLeaseSeconds`, `GitHub__Webhook__PollIntervalSeconds`, `Services__Project__BaseUrl` | `GitHub__ClientSecret`, `GitHub__PrivateKeyBase64`, `GitHub__WebhookSecret` | GitHub App (external; see [EXTERNAL_INTEGRATIONS.md](EXTERNAL_INTEGRATIONS.md)) |
| **jira** | `Jira__ClientId`, `Jira__ClientSecret`, `Jira__RedirectUri`, `Jira__SyncIntervalMinutes` | `Jira__ClientSecret` | Atlassian OAuth app (external) |
| **meeting** | *(common keys only)* | — | — |
| **submission** | `Storage__Endpoint`, `Storage__Bucket`, `Storage__AccessKey`, `Storage__SecretKey`, `Storage__MaximumFileSizeBytes` | `Storage__AccessKey`, `Storage__SecretKey` | **Reserved and not used by code yet**; Blob integration is deferred. Production allows placeholders for these keys only |
| **grafana** | `GF_SECURITY_ADMIN_USER`, `GF_SECURITY_ADMIN_PASSWORD`, `GF_USERS_ALLOW_SIGN_UP`, `GF_SERVER_ROOT_URL` | `GF_SECURITY_ADMIN_PASSWORD` | Generated. In Production the Azure Compose file also forces `GF_SECURITY_COOKIE_SECURE=true`, `GF_USERS_ALLOW_SIGN_UP=false`, `GF_AUTH_ANONYMOUS_ENABLED=false` |
| **admin** | `MYSQL_HOST`, `MYSQL_PORT`, `MYSQL_ADMIN_USER`, `MYSQL_ADMIN_PASSWORD` | password | Local development only; not a deployment secret |

## Adding a key

Follow the root `CLAUDE.md` §6:
1. Add the key to the `.env.example`.
2. Update the binding code.
3. Update `deploy/validate-env-files.sh` (Test) and/or `deploy/azure/validation/validate-azure-env-files.sh` (Production).
4. Add the key to **both** GitHub Environments' bundles **before** merging. Both validators reject bundles missing a contract key.
