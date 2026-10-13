# Database provisioning and access

Each business service owns its database, scoped MySQL user, schema and migrations.
No service should use another service's credentials or read its database.

## Before starting

1. Complete `./scripts/setup.sh` and configure shared JWT and service-local files
   as described in [local setup](../development/local-setup.md).
2. Make the configured MySQL server reachable; install the `mysql` CLI.
3. Configure each service's `Database__Host`, `Database__Port`, `Database__Name`,
   `Database__TestName`, `Database__Username`, `Database__Password`,
   `Database__SslMode` and `Database__AllowPublicKeyRetrieval`.

The physical database endpoint is environment-owned. Local scripts use the
selected service's settings. An explicit `ConnectionStrings__DefaultConnection`
is also supported by application/design-time configuration; ensure it points at
the intended database rather than an unresolved template placeholder.

## Suggested local ownership

| Service | Development database | Integration-test database | Scoped user |
| --- | --- | --- | --- |
| Auth | `researchtrack_auth` | `researchtrack_test_auth` | `rt_auth` |
| Project | `researchtrack_project` | `researchtrack_test_project` | `rt_project` |
| GitHub | `researchtrack_github` | `researchtrack_test_github` | `rt_github` |
| Jira | `researchtrack_jira` | `researchtrack_test_jira` | `rt_jira` |
| Meeting | `researchtrack_meeting` | `researchtrack_test_meeting` | `rt_meeting` |
| Submission | `researchtrack_submission` | `researchtrack_test_submission` | `rt_submission` |

Test database names must satisfy the test infrastructure's `researchtrack_test_`
naming guard. Keep development, test and deployed credentials separate.

## Administrator-only provisioning

Normal developers can skip this when databases/users already exist. The operator
needs permission to create databases/users and grant scoped access:

```bash
cp config/env/admin/.env.example config/env/admin/.env.local
chmod 600 config/env/admin/.env.local
```

Edit `MYSQL_HOST`, `MYSQL_PORT`, `MYSQL_ADMIN_USER` and `MYSQL_ADMIN_PASSWORD` in
that file. Configure all six services' database settings before running:

```bash
./scripts/db-init.sh
```

The script checks administrator connectivity, creates missing development/test
databases, creates or updates a service account, and grants access only to that
service's databases. It does not create application tables or print passwords.
Do not distribute administrator credentials to developers who do not provision databases.

## Verify service access and create schemas

```bash
./scripts/db-status.sh
./scripts/migrate.sh all
```

`db-status.sh` checks both development and test credentials for every service,
so all service files must be configured for this full check. It returns nonzero
when any check fails. It does not require the administrator file.

| Status | Checks |
| --- | --- |
| `CONNECTION FAILED` | Server reachability, hostname, port, firewall and TLS settings. |
| `AUTH FAILED` | Service username/password and MySQL grants; keep the application on its scoped user. |
| `DATABASE MISSING` | Database name and whether provisioning completed. |
| `OK` | Service credentials can access that database. |

Provisioning and migrations are separate: see [EF Core migrations](migrations.md)
for pending-migration behavior and SQL generation. For deployed environments use
the [deployment configuration/runbooks](../devops/configuration/README.md), rather
than running local provisioning commands against Production.
