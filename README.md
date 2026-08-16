# ResearchTrack Backend

ResearchTrack is an ASP.NET Core microservice backend for a final-year research supervision platform. This repository provides the shared development foundation for the backend monorepo: service boundaries, EF Core/MySQL conventions, the YARP API gateway, common API/error handling, health checks, test infrastructure, development scripts, and CI quality checks.

> **Current-stage boundary:** deployment infrastructure, production containerization, cloud configuration, monitoring deployment, and object/file storage are intentionally outside this baseline. They can be added later without changing the development configuration model described here.

---

## Technology baseline

- .NET SDK **10.0.300** via `global.json`
- Target framework: `net10.0`
- ASP.NET Core Web API
- EF Core 10
- MySQL 8+
- YARP API Gateway
- Swagger / OpenAPI
- xUnit + `WebApplicationFactory`
- GitHub Actions quality checks

NuGet versions are centralized in `Directory.Packages.props`.

---

## Architecture

```text
React frontend :5173
        |
        v
ResearchTrack Gateway :5000
        |
        +----------------+----------------+----------------+
        |                |                |                |
        v                v                v                v
      Auth             Project          GitHub            Jira
      :5101            :5102            :5103            :5104
        |                |                |                |
        v                v                v                v
   auth database    project database  github database   jira database

        +-----------------------------------------------+
        |                                               |
        v                                               v
      Meeting                                        Submission
      :5105                                          :5106
        |                                               |
        v                                               v
 meeting database                              submission database
```

The monorepo is a source-management choice, not a shared service boundary. Each business service owns its own ASP.NET project, EF Core `DbContext`, migrations, configuration, tests, and database.

### Service ports

| Component | Local port |
|---|---:|
| Gateway | `5000` |
| Auth Service | `5101` |
| Project Service | `5102` |
| GitHub Service | `5103` |
| Jira Service | `5104` |
| Meeting Service | `5105` |
| Submission Service | `5106` |
| React frontend | `5173` |

The React frontend should use the gateway only:

```text
http://localhost:5000
```

---

# Configuration model

ResearchTrack development uses three clearly separated configuration locations.

```text
.env.local
    normal local development/database configuration

.env.admin.local
    database provisioning administrator credentials only

ASP.NET User Secrets
    feature/application secrets such as JWT, GitHub, Jira, email, etc.
```

This separation prevents the MySQL administrator password from being placed in every developer's normal environment file.

## Files committed to Git

```text
.env.example
.env.admin.example
```

These files contain templates/placeholders only and must never contain real passwords.

## Files never committed

```text
.env.local
.env.admin.local
```

Both are ignored by `.gitignore`.

On macOS/Linux/WSL, keep their permissions restrictive:

```bash
chmod 600 .env.local
chmod 600 .env.admin.local
```

Normal developers usually need only `.env.local`. The database administrator needs `.env.admin.local` only when provisioning databases/users.

---

# 1. Prerequisites

Install the following on the development machine:

1. Git
2. .NET SDK compatible with `global.json` (team baseline: **10.0.300**)
3. MySQL 8+ compatible CLI client (`mysql`)
4. `curl`
5. Bash-compatible shell

### macOS / Linux

Use the normal Terminal/Bash workflow.

### Windows

The repository scripts are Bash scripts. **WSL is the recommended Windows workflow.** Run Git, .NET, the MySQL client, and these scripts from the same WSL environment so paths and environment handling remain consistent.

Verify prerequisites:

```bash
git --version
dotnet --info
mysql --version
curl --version
```

`global.json` selects the required .NET SDK family; it does not install the SDK.

---

# 2. First-time repository setup

From the repository root:

```bash
./scripts/setup.sh
```

The setup script:

- checks Git and .NET
- verifies the expected .NET 10 SDK family
- creates `.env.local` from `.env.example` if it does not already exist
- applies restrictive local permissions where supported
- validates the env-file syntax
- restores repository-local .NET tools
- restores NuGet packages
- reports whether the MySQL client and `curl` are available

It deliberately **does not**:

- install or start a database server
- create databases
- change existing credentials
- create `.env.admin.local`
- deploy anything

Existing `.env.local` files are never overwritten by `setup.sh`.

---

# 3. Configure `.env.local`

After setup, open:

```text
.env.local
```

The committed template contains:

```env
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development

MYSQL_HOST=127.0.0.1
MYSQL_PORT=3307

FRONTEND_ORIGIN=http://localhost:5173

AUTH_SERVICE_URL=http://localhost:5101/
PROJECT_SERVICE_URL=http://localhost:5102/
GITHUB_SERVICE_URL=http://localhost:5103/
JIRA_SERVICE_URL=http://localhost:5104/
MEETING_SERVICE_URL=http://localhost:5105/
SUBMISSION_SERVICE_URL=http://localhost:5106/
```

The repository makes no assumption about where the MySQL server physically runs. All database-aware scripts and services use only:

```text
MYSQL_HOST
MYSQL_PORT
```

Set those values to the MySQL endpoint available in your development environment.

## Service database configuration

Each service has one development database and one isolated integration-test database.

Example:

```env
AUTH_DB_NAME=researchtrack_auth
AUTH_TEST_DB_NAME=researchtrack_test_auth
AUTH_DB_USER=rt_auth
AUTH_DB_PASSWORD=CHANGE_ME
```

The full set is:

| Service | Development DB | Test DB | User |
|---|---|---|---|
| Auth | `researchtrack_auth` | `researchtrack_test_auth` | `rt_auth` |
| Project | `researchtrack_project` | `researchtrack_test_project` | `rt_project` |
| GitHub | `researchtrack_github` | `researchtrack_test_github` | `rt_github` |
| Jira | `researchtrack_jira` | `researchtrack_test_jira` | `rt_jira` |
| Meeting | `researchtrack_meeting` | `researchtrack_test_meeting` | `rt_meeting` |
| Submission | `researchtrack_submission` | `researchtrack_test_submission` | `rt_submission` |

Replace every `CHANGE_ME` value with the real service password provided through your team's secure credential-sharing process.

The scripts refuse DB-dependent operations while placeholder passwords remain.

> Do not send `.env.local` through Git commits, pull requests, Jira comments, or other public/shared project history. If it is accidentally committed, rotate every exposed credential.

---

# 4. Database administrator configuration

This section is only for the person responsible for provisioning ResearchTrack databases and service users.

Normal developers can skip it.

Create:

```bash
cp .env.admin.example .env.admin.local
chmod 600 .env.admin.local
```

Edit `.env.admin.local`:

```env
MYSQL_ADMIN_USER=root
MYSQL_ADMIN_PASSWORD=CHANGE_ME
```

Replace `CHANGE_ME` with the real MySQL administrator password.

`.env.admin.local` is used only by:

```bash
./scripts/db-init.sh
```

It is not needed by `run.sh`, `dev.sh`, migrations, application services, or normal database status checks.

Never distribute the administrator credentials to team members who do not need provisioning access.

---

# 5. Provision databases and service users

**Administrator only.**

Ensure both files are configured:

```text
.env.local
.env.admin.local
```

Then run:

```bash
./scripts/db-init.sh
```

The script:

1. loads the configured MySQL host/port from `.env.local`
2. loads the administrator username/password from `.env.admin.local`
3. verifies administrator connectivity
4. creates the six development databases if missing
5. creates the six test databases if missing
6. creates/updates one scoped MySQL account per service
7. grants each account privileges only to that service's development and test databases

It does not print database passwords.

It creates database ownership equivalent to:

```text
rt_auth       -> researchtrack_auth + researchtrack_test_auth
rt_project    -> researchtrack_project + researchtrack_test_project
rt_github     -> researchtrack_github + researchtrack_test_github
rt_jira       -> researchtrack_jira + researchtrack_test_jira
rt_meeting    -> researchtrack_meeting + researchtrack_test_meeting
rt_submission -> researchtrack_submission + researchtrack_test_submission
```

Application tables are **not** created by `db-init.sh`; schemas are owned by EF Core migrations.

---

# 6. Verify database access

Normal developers can verify all service credentials with:

```bash
./scripts/db-status.sh
```

Example output:

```text
MySQL endpoint: 127.0.0.1:3307

SERVICE          DATABASE                           STATUS
---------------- ---------------------------------- ----------------------
auth/dev         researchtrack_auth                 OK
auth/test        researchtrack_test_auth            OK
project/dev      researchtrack_project              OK
project/test     researchtrack_test_project         OK
...
```

Possible status values include:

```text
OK
CONNECTION FAILED
AUTH FAILED
DATABASE MISSING
FAILED
```

This script uses only the service credentials from `.env.local`. It never needs the MySQL administrator account.

---

# 7. EF Core migrations

Every business service owns its own `DbContext` and migration history.

```text
AuthDbContext
ProjectDbContext
GitHubDbContext
JiraDbContext
MeetingDbContext
SubmissionDbContext
```

There is no shared `ResearchTrackDbContext`.

## Apply one service's migrations

```bash
./scripts/migrate.sh auth
./scripts/migrate.sh project
```

## Apply all service migrations

```bash
./scripts/migrate.sh all
```

## Add a migration

```bash
./scripts/migration-add.sh project AddProjectSchema
```

## List migrations

```bash
./scripts/migration-list.sh project
```

## Generate idempotent migration SQL

```bash
./scripts/migration-script.sh project
```

By default the generated file is written to:

```text
artifacts/migrations/project.sql
```

Migration scripts resolve their connection strings entirely from `.env.local`.

> EF Core `Migrations/` directories are source code and must be committed to Git.

---

# 8. Running the backend

## Run one project in the foreground

```bash
./scripts/run.sh gateway
./scripts/run.sh auth
./scripts/run.sh project
./scripts/run.sh github
./scripts/run.sh jira
./scripts/run.sh meeting
./scripts/run.sh submission
```

For business services, `run.sh` builds `ConnectionStrings__DefaultConnection` from `.env.local` and exports it only to that process.

For the gateway, `run.sh` loads the configured frontend origin and service URLs from `.env.local`.

## Run the core/Sprint 1 stack

```bash
./scripts/dev.sh core
```

Starts:

```text
Auth
Project
Gateway
```

## Run integration service skeletons

```bash
./scripts/dev.sh integrations
```

Starts:

```text
Auth
Project
GitHub
Jira
Gateway
```

## Run research service skeletons

```bash
./scripts/dev.sh research
```

Starts:

```text
Auth
Project
Meeting
Submission
Gateway
```

## Run all backend services

```bash
./scripts/dev.sh all
```

Background process information is stored under the gitignored `.run/` directory.

Logs:

```text
.run/logs/auth.log
.run/logs/project.log
.run/logs/gateway.log
...
```

Stop processes:

```bash
./scripts/stop.sh core
./scripts/stop.sh integrations
./scripts/stop.sh research
./scripts/stop.sh all
```

---

# 9. Health checks

Every service exposes:

```text
GET /health/live
GET /health/ready
```

`live` means the ASP.NET process is running.

`ready` means the application is ready to serve requests; business services also verify their database connection.

Check a profile:

```bash
./scripts/health.sh core
./scripts/health.sh all
```

Example:

```text
SERVICE      LIVE       READY
------------ ---------- ----------
gateway      OK         OK
auth         OK         OK
project      OK         OK
```

If `LIVE=OK` but `READY=FAIL`, inspect the service log and verify `.env.local` plus database connectivity.

---

# 10. Swagger / OpenAPI

Swagger is enabled in Development/Testing.

Typical URLs:

```text
Gateway     http://localhost:5000/swagger
Auth        http://localhost:5101/swagger
Project     http://localhost:5102/swagger
GitHub      http://localhost:5103/swagger
Jira        http://localhost:5104/swagger
Meeting     http://localhost:5105/swagger
Submission  http://localhost:5106/swagger
```

---

# 11. Testing

## Normal tests

Run all non-database-integration tests:

```bash
./scripts/test.sh
```

Run one service test project:

```bash
./scripts/test.sh auth
./scripts/test.sh project
```

These commands do not require `.env.local` database connectivity.

## Database integration tests

Run explicitly:

```bash
./scripts/test.sh integration
```

This command loads `.env.local` and exports dedicated test connection strings using:

```text
researchtrack_test_auth
researchtrack_test_project
researchtrack_test_github
researchtrack_test_jira
researchtrack_test_meeting
researchtrack_test_submission
```

Development databases must not be used for destructive integration-test cleanup.

---

# 12. Pre-PR quality check

Before opening a pull request:

```bash
./scripts/check.sh
```

It performs:

```text
dotnet tool restore
restore
Release build
non-database integration/unit tests
coverage collection
format verification
```

Database integration tests remain explicit:

```bash
./scripts/test.sh integration
```

This keeps normal quality checks independent from external database availability.

---

# 13. ASP.NET User Secrets

Do not place future application secrets in `.env.local` merely because they are secrets.

Use ASP.NET User Secrets for feature/application values such as:

```text
JWT signing keys
GitHub application/client secrets
Jira OAuth secrets
email provider credentials
future storage credentials
```

Store a secret:

```bash
./scripts/secrets-set.sh auth "Jwt:SigningKey"
```

The value is requested interactively and is not put into shell history by the script.

List configured keys with values masked:

```bash
./scripts/secrets-list.sh auth
```

Remove one key:

```bash
./scripts/secrets-remove.sh auth "Jwt:SigningKey"
```

Clear all User Secrets for a project:

```bash
./scripts/secrets-clear.sh auth
```

User Secrets are development-only configuration and are stored outside the repository.

---

# 14. Environment file format

The env loader deliberately treats env files as **data rather than executable shell scripts**.

Supported:

```text
KEY=value
KEY="value"
KEY='value'
# comments
blank lines
```

Do not use executable shell syntax such as:

```text
export KEY=value
KEY=$(command)
KEY=`command`
```

The scripts do not `source .env.local`; they parse it as a simple key/value file.

This design also accepts Windows CRLF line endings when files are edited from Windows/WSL workflows.

---

# 15. Secret-handling rules

Never commit or share publicly:

```text
.env.local
.env.admin.local
service database passwords
MySQL administrator password
private keys
JWT signing keys
GitHub/Jira OAuth secrets
future provider credentials
```

Safe repository templates:

```text
.env.example
.env.admin.example
```

Additional rules:

1. Give the administrator password only to people who actually provision the DB server.
2. Normal application processes use service-specific DB accounts, not the MySQL administrator account.
3. Each service DB account receives access only to its own development/test databases.
4. Do not print database passwords in scripts or logs.
5. Rotate credentials immediately if a secret enters Git history or another uncontrolled location.
6. Do not reuse development passwords in staging/production.

---

# 16. Common command reference

```bash
# First setup
./scripts/setup.sh

# Database provisioning - administrator only
./scripts/db-init.sh

# Normal DB verification
./scripts/db-status.sh

# EF Core
./scripts/migrate.sh all
./scripts/migration-add.sh project AddProjectSchema
./scripts/migration-list.sh project
./scripts/migration-script.sh project

# Development
./scripts/run.sh project
./scripts/dev.sh core
./scripts/health.sh core
./scripts/stop.sh core

# Quality
./scripts/build.sh
./scripts/test.sh
./scripts/test.sh integration
./scripts/check.sh
./scripts/format.sh
./scripts/clean.sh

# User Secrets
./scripts/secrets-set.sh auth "Jwt:SigningKey"
./scripts/secrets-list.sh auth
./scripts/secrets-remove.sh auth "Jwt:SigningKey"
./scripts/secrets-clear.sh auth
```

---

# 17. Troubleshooting

## `Missing .env.local`

Run:

```bash
./scripts/setup.sh
```

Then configure the copied `.env.local`.

## `Environment variable ... still contains a placeholder value`

A DB password still has:

```text
CHANGE_ME
```

Replace it with the real service credential.

## `Missing .env.admin.local`

Only `db-init.sh` requires this file.

If you are the database administrator:

```bash
cp .env.admin.example .env.admin.local
chmod 600 .env.admin.local
```

Then configure the administrator credentials.

Normal developers should not create this file unless they are responsible for provisioning.

## `CONNECTION FAILED`

Verify:

```text
MYSQL_HOST
MYSQL_PORT
```

and confirm that the configured MySQL endpoint is reachable.

## `AUTH FAILED`

Verify the service's:

```text
*_DB_USER
*_DB_PASSWORD
```

values.

Do not solve this by switching the application to the MySQL administrator account.

## `DATABASE MISSING`

The configured database has not been provisioned or its name is wrong. Ask the database administrator to verify provisioning.

## EF Core cannot create the DbContext

Use the repository migration scripts rather than manually invoking `dotnet ef`; the scripts export the correct connection string for the selected service.

## Service is LIVE but not READY

Check:

```bash
./scripts/db-status.sh
```

and inspect:

```text
.run/logs/<service>.log
```

---

# 18. Repository configuration principles

The configuration model is intentionally infrastructure-agnostic:

```text
ResearchTrack application/scripts
             |
             v
      MYSQL_HOST:MYSQL_PORT
             |
             v
           MySQL
```

The backend does not need to know how that endpoint is provided.

Database provisioning is intentionally separate:

```text
.env.local
    +
.env.admin.local
    |
    v
 db-init.sh
    |
    v
MySQL provisioning
```

Normal development uses:

```text
.env.local
    |
    +--> run.sh
    +--> dev.sh
    +--> db-status.sh
    +--> migration scripts
    +--> database integration tests
```

Future deployment configuration can use the platform's own secure environment/secrets system without changing these application configuration keys.
