# ResearchTrack environment configuration

`config/env` is the **single source of truth for runtime environment-variable contracts**.

Each runtime component owns one committed `.env.example`:

```text
config/env/
├── admin/.env.example       # local DB provisioning only
├── mysql/.env.example       # remote Compose MySQL contract
├── shared/.env.example      # JWT values shared by Auth + Project
├── gateway/.env.example
├── auth/.env.example
├── project/.env.example
├── github/.env.example
├── jira/.env.example
├── meeting/.env.example
└── submission/.env.example
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

Copy the complete example shape, then change values for the target environment. Do not commit the completed files.

Remote application files must use:

- Test: `ASPNETCORE_ENVIRONMENT=Test`, `DOTNET_ENVIRONMENT=Test`, `ASPNETCORE_URLS=http://+:8080`
- Production: `ASPNETCORE_ENVIRONMENT=Production`, `DOTNET_ENVIRONMENT=Production`, `ASPNETCORE_URLS=http://+:8080`
- DB services: `ConnectionStrings__DefaultConnection` with `Server=mysql`, `Port=3306`, and `SslMode=Disabled`
- Project: `Services__Auth__BaseUrl=http://auth:8080`
- Gateway internal URLs: `http://<compose-service>:8080`

`deploy/validate-env-files.sh` verifies that every runtime deployment file still contains every key in its canonical `.env.example` and then performs deployment-specific consistency checks.

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

Auth and Project receive the same `Jwt__Issuer`, `Jwt__Audience`, and `Jwt__SigningKey` from `config/env/shared/.env.example`. Test and Production must use different signing keys.

## Future integration keys

Some service examples contain currently optional keys reserved for the later GitHub/Jira/Kafka/object-storage implementation. They remain in the canonical service contract but may be empty until the corresponding feature is implemented. Deployment validation only requires currently operational settings to be populated.

## Security rules

- Commit `.env.example` only.
- Never commit `.env.local` or completed deployment env files.
- Keep Test and Production credentials different.
- Use a unique DB user/password per service.
- Use a unique JWT signing key per deployed environment.
- GitHub Actions materializes deployment files with restrictive permissions and uploads them to the VPS over SSH.
