# ResearchTrack environment configuration

`config/env` is the **single source of truth for runtime environment-variable contracts**.

Each runtime component owns one committed `.env.example`:

```text
config/env/
├── admin/.env.example       # local DB provisioning only
├── mysql/.env.example       # remote Compose MySQL contract
├── shared/.env.example      # JWT values shared by all six business services
├── gateway/.env.example
├── auth/.env.example
├── project/.env.example
├── github/.env.example
├── jira/.env.example
├── meeting/.env.example
├── submission/.env.example
└── grafana/.env.example
```

There is deliberately **no second set of deployment templates under `deploy/`**.

## Local development

Run:

```bash
./scripts/setup.sh
```

The setup script copies the committed examples to gitignored `.env.local` files for `shared`, `gateway`, and each application service. It never overwrites an existing `.env.local`.

Example:

```text
config/env/auth/.env.example   # committed contract
config/env/auth/.env.local     # developer values, never committed
```

For local DB provisioning, create `config/env/admin/.env.local` manually from its example and run `./scripts/db-init.sh`.

## Test and production deployment

The same committed contracts are used to prepare GitHub Environment multiline secrets. The secret names are:

| Canonical contract | GitHub Environment secret | VPS runtime file |
|---|---|---|
| `mysql/.env.example` | `MYSQL_ENV_FILE` | `mysql.env` |
| `shared/.env.example` | `SHARED_AUTH_ENV_FILE` | `shared-auth.env` |
| `gateway/.env.example` | `GATEWAY_ENV_FILE` | `gateway.env` |
| `auth/.env.example` | `AUTH_ENV_FILE` | `auth.env` |
| `project/.env.example` | `PROJECT_ENV_FILE` | `project.env` |
| `github/.env.example` | `RT_GITHUB_SERVICE_ENV` | `github.env` |
| `jira/.env.example` | `JIRA_ENV_FILE` | `jira.env` |
| `meeting/.env.example` | `MEETING_ENV_FILE` | `meeting.env` |
| `submission/.env.example` | `SUBMISSION_ENV_FILE` | `submission.env` |
| `grafana/.env.example` | `GRAFANA_ENV_FILE` | `grafana.env` |

Copy the complete example shape, then change values for the target environment. Do not commit the completed files.

The two deployed environments apply different rules to the same keys:

- **Test** (`develop` → VPS + Docker Compose): `ASPNETCORE_ENVIRONMENT=Test`, `DOTNET_ENVIRONMENT=Test`, `ASPNETCORE_URLS=http://+:8080`. DB services use `Server=mysql;Port=3306;…;SslMode=Disabled`. Internal URLs use `http://<compose-service>:8080` (e.g. `Services__Auth__BaseUrl=http://auth:8080`). Enforced by `deploy/validate-env-files.sh`.
- **Production** (`main` → Azure Container Apps + infrastructure VM): `ASPNETCORE_ENVIRONMENT=Production`, `DOTNET_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:8080`. DB services use `Server=10.20.10.4;Port=3306;…;SslMode=Required`. Internal URLs use `http://rt-<service>-prod`. Public URLs must be HTTPS. Enforced by `deploy/azure/validation/validate-azure-env-files.sh`.

Both validators require every key of the canonical `.env.example` to be present. The full per-environment configuration guide (every GitHub secret/variable, sources, consumers) is in [`docs/devops/configuration/`](../../docs/devops/configuration/README.md).

## Gateway contract

The Gateway intentionally uses friendly variables in its canonical contract:

```text
FRONTEND_ORIGIN
AUTH_SERVICE_URL
PROJECT_SERVICE_URL
GITHUB_SERVICE_URL
JIRA_SERVICE_URL
MEETING_SERVICE_URL
SUBMISSION_SERVICE_URL
```

Local scripts map those values into the ASP.NET/YARP hierarchy. The Gateway also performs the same mapping at application startup, so Docker deployment can inject the exact same contract directly.

## Shared JWT configuration

Auth, Project, GitHub, Jira, Meeting, and Submission receive the same `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey` from `config/env/shared/.env.example`. Test and Production must use different signing keys.

## Storage and integration settings

Submission object storage is implemented. Populate the service's required
`Storage__*` and `Submission__*` settings for the actual bucket/provider; an
endpoint may be omitted where the SDK uses the configured region's AWS endpoint.
Consult [service configuration](../../docs/development/service-configuration.md)
and the canonical template for required versus optional values. A validator
accepting a contract shape is not proof that provider credentials or operations work.

Deployment validators enforce environment-specific rules and placeholder checks.
Use their current output and the service's startup validation rather than assuming
older reserved-key exceptions still apply. Kafka has its own optional enablement
rules below.

## Security rules

- Commit `.env.example` only.
- Never commit `.env.local` or completed deployment env files.
- Keep Test and Production credentials different.
- Use a unique DB user/password per service.
- Use a unique JWT signing key per deployed environment.
- GitHub Actions materializes deployment files with restrictive permissions: for Test it uploads them to the VPS over SSH; for Production it turns them into Azure Container App secrets/variables and, for MySQL/Grafana, a protected Azure Run Command parameter.

## Optional Kafka transport readiness

GitHub and Jira examples now include disabled-by-default `Kafka__*` settings. Older
runtime bundles may omit these keys. When enabled, validators require a reviewed
topic/contract in the central registry, an external TLS endpoint and real CA delivery.
Production uses the VM RFC1918 private IPv4 listener on 9092. The public CA Base64 is
delivered through the existing Container App secret system and written to the validated
runtime CA path before startup. GitHub/Jira now have real transactional-outbox producers
and consumers for the owner-approved webhook-reference contracts. Their examples contain
the approved topic, version `1` and mandatory consumer group; keep enabled `false` until
isolated and live topic/trust checks pass. The remaining blank CA must come from the
existing broker public certificate. See
[Kafka topics and event flows](../../docs/devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md).
