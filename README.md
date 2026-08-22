# ResearchTrack Backend

ResearchTrack is an ASP.NET Core microservice backend for a final-year research supervision platform. This repository provides the shared development foundation for the backend monorepo: service boundaries, EF Core/MySQL conventions, the YARP API gateway, common API/error handling, health checks, test infrastructure, development scripts, CI quality checks, and the two-branch DevOps workflow.

> **Current-stage boundary:** provider-specific cloud deployment commands, production container registry configuration, monitoring deployment, and object/file storage provisioning are intentionally outside this baseline. GitHub Actions records the Test and Production deployment gates and can be connected to the team's hosting provider when credentials are available.

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

---

## Git strategy and CI/CD

ResearchTrack uses `develop` for integrated sprint work and Test deployment candidates, and `main` for production-ready releases.

- Pull requests to `develop` and `main` run restore, build, non-database test, format, and publish checks.
- Pushes to `develop` represent the backend Test deployment candidate.
- Pushes to `main` represent the backend Production deployment candidate.
- Deployment jobs use GitHub Environments named `test` and `production`; provider-specific Docker publishing and deployment commands can be added after infrastructure credentials are configured.
- Test/production runtime values should be injected through the CI/CD or deployment platform's environment/secrets mechanism. They must not be committed as real `.env` files.

For branching rules, merge gates, and release approval requirements, see `CONTRIBUTING.md` and `docs/devops/branching-strategy.md`.

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

The monorepo is a source-management choice, not a shared service boundary. Each business service owns its own ASP.NET project, EF Core `DbContext`, migrations, runtime configuration, tests, and database.

### Service ports

| Component | Local port | Responsibility |
|---|---:|---|
| Gateway | `5000` | Public API entry point and reverse proxy |
| Auth Service | `5101` | Registration, authentication and user identity |
| Project Service | `5102` | Research projects and memberships |
| GitHub Service | `5103` | GitHub integration and synchronized evidence |
| Jira Service | `5104` | Jira integration and synchronized progress |
| Meeting Service | `5105` | Supervision meetings |
| Submission Service | `5106` | Research submissions and versions |
| React frontend | `5173` | User interface |

The React frontend should use the gateway only:

```text
http://localhost:5000
```

### Configuration boundary

The service boundary also applies to configuration:

```text
Auth Service       <- config/env/auth/.env.local
Project Service    <- config/env/project/.env.local
GitHub Service     <- config/env/github/.env.local
Jira Service       <- config/env/jira/.env.local
Meeting Service    <- config/env/meeting/.env.local
Submission Service <- config/env/submission/.env.local
Gateway            <- config/env/gateway/.env.local
```

A service should receive only the runtime values it owns. Sharing infrastructure code does not mean sharing configuration values or database credentials.

---

# Configuration model

ResearchTrack uses a **per-service environment configuration model**. Runtime values are external to source code and are not centralized in one root `.env` file.

```text
config/env/
├── admin/
│   ├── .env.example
│   └── .env.local        # local only, gitignored
├── gateway/
│   ├── .env.example
│   └── .env.local
├── auth/
│   ├── .env.example
│   └── .env.local
├── project/
│   ├── .env.example
│   └── .env.local
├── github/
│   ├── .env.example
│   └── .env.local
├── jira/
│   ├── .env.example
│   └── .env.local
├── meeting/
│   ├── .env.example
│   └── .env.local
└── submission/
    ├── .env.example
    └── .env.local
```

The rules are:

1. **`.env.example` is the committed configuration contract.** It contains keys, comments and placeholders only.
2. **`.env.local` contains actual developer-local values.** It is never committed.
3. **Test/production values are injected externally** by CI/CD or the deployment platform rather than stored as real environment files in Git.
4. **Secrets and configurable business policy are both externalized.** Institutional email rules, password policy, synchronization intervals, storage limits, OAuth credentials, DB credentials, and similar runtime policy belong in ENV configuration.
5. **Stable domain invariants remain in code.** For example, ResearchTrack having `STUDENT` and `SUPERVISOR` roles is a domain rule; which institutional domain maps to each role is configurable policy.
6. **No service should depend on another service's `.env.local`.** Each component receives only its own configuration.

`appsettings.json` may still contain stable framework/application structure such as logging defaults, service identity, gateway route definitions, and empty YARP destination slots. Real runtime endpoints, credentials and configurable business-policy values must come from ENV.

For a shorter configuration reference, see `config/env/README.md`.

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
- creates a missing `.env.local` beside every committed service `.env.example`
- never overwrites an existing service `.env.local`
- applies restrictive local permissions where supported
- restores repository-local .NET tools
- restores NuGet packages

It creates local files for:

```text
config/env/gateway/.env.local
config/env/auth/.env.local
config/env/project/.env.local
config/env/github/.env.local
config/env/jira/.env.local
config/env/meeting/.env.local
config/env/submission/.env.local
```

It deliberately **does not**:

- install or start a database server
- create databases or MySQL users
- create `config/env/admin/.env.local`
- replace `CHANGE_ME` values
- overwrite existing local configuration
- deploy anything

After setup, configure only the services you intend to run.

On macOS/Linux/WSL, keep local files restrictive:

```bash
chmod 600 config/env/auth/.env.local
chmod 600 config/env/project/.env.local
```

---

# 3. Configure service-owned `.env.local` files

Each runtime component has its own environment contract.

## Gateway

```text
config/env/gateway/.env.local
```

Important keys:

```env
ASPNETCORE_ENVIRONMENT=Development
DOTNET_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5000
OpenApi__Enabled=true

FRONTEND_ORIGIN=...
AUTH_SERVICE_URL=http://localhost:5101/
PROJECT_SERVICE_URL=http://localhost:5102/
GITHUB_SERVICE_URL=http://localhost:5103/
JIRA_SERVICE_URL=http://localhost:5104/
MEETING_SERVICE_URL=http://localhost:5105/
SUBMISSION_SERVICE_URL=http://localhost:5106/
```

The committed gateway route structure remains in `appsettings.json`; the real destination URLs are supplied by ENV.

## Business-service database configuration

Every business service owns its own DB values:

```text
Database__Host
Database__Port
Database__Name
Database__TestName
Database__Username
Database__Password
Database__SslMode
Database__AllowPublicKeyRetrieval
```

Example service file:

```text
config/env/project/.env.local
```

Example shape:

```env
Database__Host=127.0.0.1
Database__Port=3307
Database__Name=researchtrack_project
Database__TestName=researchtrack_test_project
Database__Username=rt_project
Database__Password=...
Database__SslMode=...
Database__AllowPublicKeyRetrieval=...
```

The repository makes no assumption about where MySQL physically runs. Each service uses the endpoint supplied to that service.

`ConnectionStrings__DefaultConnection` is supported as an explicit deployment/design-time override. Otherwise the shared database connection-string resolver constructs the connection string from that service's injected `Database__*` variables.

### Database ownership

A typical local naming scheme is:

| Service | Development DB | Integration-test DB | Suggested scoped user |
|---|---|---|---|
| Auth | `researchtrack_auth` | `researchtrack_test_auth` | `rt_auth` |
| Project | `researchtrack_project` | `researchtrack_test_project` | `rt_project` |
| GitHub | `researchtrack_github` | `researchtrack_test_github` | `rt_github` |
| Jira | `researchtrack_jira` | `researchtrack_test_jira` | `rt_jira` |
| Meeting | `researchtrack_meeting` | `researchtrack_test_meeting` | `rt_meeting` |
| Submission | `researchtrack_submission` | `researchtrack_test_submission` | `rt_submission` |

The actual values are intentionally not committed into the `.env.example` files; each team's local/test deployment supplies them externally.

## Service-specific configuration

In addition to database values, services own their integration/policy configuration.

### Auth

```text
config/env/auth/.env.local
```

Contains Story 1 registration policy, password policy, password hashing values, and reserved JWT keys for the later login/authentication story.

### Project

```text
Kafka__BootstrapServers
```

### GitHub

```text
Kafka__BootstrapServers
GitHub__AppId
GitHub__ClientId
GitHub__ClientSecret
GitHub__PrivateKeyPath
GitHub__WebhookSecret
GitHub__SyncIntervalMinutes
```

### Jira

```text
Kafka__BootstrapServers
Jira__ClientId
Jira__ClientSecret
Jira__RedirectUri
Jira__SyncIntervalMinutes
```

### Meeting

```text
Kafka__BootstrapServers
```

### Submission

```text
Kafka__BootstrapServers
Storage__Endpoint
Storage__Bucket
Storage__AccessKey
Storage__SecretKey
Storage__MaximumFileSizeBytes
```

Real provider values must stay outside Git.

---

# 4. Database administrator configuration

This section is only for the person responsible for provisioning ResearchTrack databases and scoped service users.

Normal developers can skip it after provisioning is complete.

Create:

```bash
cp config/env/admin/.env.example config/env/admin/.env.local
chmod 600 config/env/admin/.env.local
```

Edit:

```text
config/env/admin/.env.local
```

Contract:

```env
MYSQL_HOST=...
MYSQL_PORT=...
MYSQL_ADMIN_USER=...
MYSQL_ADMIN_PASSWORD=...
```

`config/env/admin/.env.local` is used only by:

```bash
./scripts/db-init.sh
```

It is not needed by normal service startup, migrations, health checks, ordinary DB status checks, or normal application requests.

Never distribute administrator credentials to team members who do not need database provisioning access.

---

# 5. Provision databases and service users

**Administrator only.**

Before provisioning:

1. configure `config/env/admin/.env.local`
2. configure the `Database__*` values in each business service `.env.local`

Then run:

```bash
./scripts/db-init.sh
```

The script:

1. loads the administrator endpoint/credentials from `config/env/admin/.env.local`
2. verifies administrator connectivity
3. loads each business service's own `.env.local`
4. creates that service's development database if missing
5. creates that service's integration-test database if missing
6. creates/updates one scoped MySQL account per service
7. grants that account privileges only to its service-owned development/test databases
8. does not print database passwords

Conceptually:

```text
admin/.env.local
       |
       v
   db-init.sh
       |
       +--> auth/.env.local       -> Auth DB + scoped Auth user
       +--> project/.env.local    -> Project DB + scoped Project user
       +--> github/.env.local     -> GitHub DB + scoped GitHub user
       +--> jira/.env.local       -> Jira DB + scoped Jira user
       +--> meeting/.env.local    -> Meeting DB + scoped Meeting user
       +--> submission/.env.local -> Submission DB + scoped Submission user
```

Application tables are **not** created by `db-init.sh`; schemas remain owned by EF Core migrations.

---

# 6. Verify database access

Normal developers can verify every service's development and test credentials with:

```bash
./scripts/db-status.sh
```

Example output:

```text
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

This script uses only service-scoped credentials. It never needs the MySQL administrator account.

---

# 7. EF Core migrations

Every business service owns its own `DbContext` and migration history:

```text
AuthDbContext
ProjectDbContext
GitHubDbContext
JiraDbContext
MeetingDbContext
SubmissionDbContext
```

There is no shared `ResearchTrackDbContext`.

Each design-time `DbContextFactory` resolves the database connection from the **selected service's environment**, through the shared connection-string resolver. Sharing this resolver does not share database values between services.

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

The migration scripts load only the target service's `.env.local` before invoking EF Core.

> EF Core `Persistence/Migrations/` directories are source code and must be committed to Git.

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

`run.sh <service>` loads only:

```text
config/env/<service>/.env.local
```

For business services it validates DB configuration before startup. For the gateway it validates gateway service URLs/origin configuration.

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

If `LIVE=OK` but `READY=FAIL`, inspect the service log and verify that service's `.env.local` plus its database connectivity.

---

# 10. Swagger / OpenAPI

Swagger is controlled through each component's environment configuration:

```text
OpenApi__Enabled
```

Typical local URLs:

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

# 11. Sprint 1 Story 1 — User Registration & Automatic Role Assignment

Story 1 is implemented in `ResearchTrack.AuthService`.

> **Story:** As a new ResearchTrack user, I want to register using my valid institutional email address so that the system can create my account and automatically assign the correct Student or Supervisor role.

## Public endpoints

Through the gateway:

```text
GET  /api/v1/auth/register/config
POST /api/v1/auth/register
```

Direct local Auth Service equivalents are available on port `5101` while developing the service.

## Registration request

Canonical ResearchTrack fields:

```json
{
  "firstName": "...",
  "lastName": "...",
  "email": "...",
  "registrationNumber": "...",
  "password": "..."
}
```

For frontend compatibility during migration from SuperviseSuite, legacy `fname`, `lname`, and `name` aliases are accepted by the request contract.

A client-supplied `role` may also arrive from a legacy payload, but it is **never trusted as authorization input**. The backend owns role assignment.

## Environment-driven registration policy

The Auth Service reads Story 1 policy from:

```text
config/env/auth/.env.local
```

Required registration-policy keys:

```text
Registration__StudentEmailDomain
Registration__SupervisorEmailDomain
Registration__StudentIdentifierPattern
Registration__RequireStudentRegistrationNumber
Registration__RequireStudentRegistrationNumberToMatchEmail
Registration__MaxFirstNameLength
Registration__MaxLastNameLength
Registration__MaxEmailLength
Registration__MaxRegistrationNumberLength
```

Password-policy keys:

```text
PasswordPolicy__MinimumLength
PasswordPolicy__MaximumLength
PasswordPolicy__RequireUppercase
PasswordPolicy__RequireLowercase
PasswordPolicy__RequireDigit
PasswordPolicy__RequireSpecialCharacter
```

Password-hashing keys:

```text
PasswordHashing__Iterations
PasswordHashing__SaltSizeBytes
PasswordHashing__HashSizeBytes
```

No real institutional domain, student-identifier regex, password-policy threshold, or hashing cost is committed as a production business value in source code.

The `.env.example` file documents only the required keys/placeholders. Actual values live in the local/deployment environment.

## Role assignment

The service owns the final decision:

```text
submitted institutional email
           |
           v
normalized + validated
           |
           v
configured institutional rules
           |
      +----+----+
      |         |
      v         v
   STUDENT   SUPERVISOR
```

The user never manually chooses a privileged role.

## Validation and security behavior

Story 1 currently provides:

- required-field validation
- email syntax validation
- institutional-domain validation
- automatic Student/Supervisor role derivation
- configurable student registration-number requirement
- configurable registration-number/email identifier matching
- configurable server-side password policy
- email normalization to lowercase before persistence
- student registration-number normalization to uppercase
- duplicate email prevention
- duplicate registration-number prevention where applicable
- database unique indexes as a final concurrency-safe duplicate barrier
- PBKDF2-SHA256 password hashing with random salts and ENV-controlled work factors
- no plaintext password persistence or response exposure
- standardized API error responses with field-level validation details
- safe registration response data only

## Acceptance-criteria mapping

| Acceptance criterion | Backend implementation |
|---|---|
| AC1 — Successful Registration | Valid request creates a persisted user and returns success data |
| AC2 — Automatic Role Assignment | Role is derived server-side from configured institutional rules |
| AC3 — Invalid Institutional Email | Invalid/non-matching institutional email is rejected with validation error |
| AC4 — Duplicate Account Prevention | Service checks plus DB unique constraint prevent duplicate email accounts |
| AC5 — Required Field Validation | Missing/invalid fields return standardized field-level validation messages |

## Auth database

`AuthDbContext` now owns the User aggregate/table and the Story 1 migration:

```text
Persistence/Migrations/20260822083000_AddUsers.cs
```

The user schema stores identity/profile fields, server-assigned role, password hash, optional student registration number, and timestamps.

## Registration configuration endpoint

```text
GET /api/v1/auth/register/config
```

This endpoint allows the already-built frontend to consume non-secret registration constraints from the same server-owned policy instead of duplicating validation values in frontend source.

It must never expose secrets such as database passwords, hashing material, JWT signing keys, or provider credentials.

## Story 1 detailed notes

See:

```text
docs/development/story-01-registration.md
```

for the endpoint contract, acceptance-criteria mapping, security notes, and verification commands.

### Story boundary

Story 1 does **not** require email OTP/ownership verification, MFA, JWT login, or refresh tokens. JWT environment keys are reserved for the later secure-login story and should not be interpreted as implemented authentication functionality yet.

---

# 12. API response conventions

ResearchTrack uses the shared API/error response infrastructure from `ResearchTrack.BuildingBlocks.Api`.

Successful responses follow the shared envelope, for example:

```json
{
  "success": true,
  "data": {},
  "meta": {
    "traceId": "...",
    "timestamp": "..."
  }
}
```

Validation responses expose field-level errors so the frontend can bind messages to individual form controls.

The shared building blocks centralize technical conventions such as response envelopes, exception handling, configuration binding helpers, and DB connection-string construction. They do **not** centralize service-owned business data or database credentials.

---

# 13. Testing

## Normal tests

Run all non-database-integration tests:

```bash
./scripts/test.sh
# equivalent explicit scope:
./scripts/test.sh all
```

Run one service test project:

```bash
./scripts/test.sh auth
./scripts/test.sh project
./scripts/test.sh github
./scripts/test.sh jira
./scripts/test.sh meeting
./scripts/test.sh submission
./scripts/test.sh gateway
```

These commands exclude tests categorized as `DatabaseIntegration`.

## Database integration tests

Run explicitly:

```bash
./scripts/test.sh integration
```

The integration command builds dedicated test connection strings from each business service's own `.env.local` using:

```text
Database__TestName
```

and exports isolated test connections for:

```text
RESEARCHTRACK_TEST_AUTH_CONNECTION
RESEARCHTRACK_TEST_PROJECT_CONNECTION
RESEARCHTRACK_TEST_GITHUB_CONNECTION
RESEARCHTRACK_TEST_JIRA_CONNECTION
RESEARCHTRACK_TEST_MEETING_CONNECTION
RESEARCHTRACK_TEST_SUBMISSION_CONNECTION
```

Development databases must not be used for destructive integration-test cleanup.

## Story 1 test coverage

The Auth tests cover the registration/configuration behavior including:

- valid Student registration
- valid Supervisor registration
- invalid institutional email
- required-field errors
- configurable password policy
- server-owned role assignment
- ignoring client-supplied role as authority
- legacy frontend field aliases
- password hashing behavior
- duplicate prevention
- database integration behavior where categorized accordingly

---

# 14. Pre-PR quality check

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

This keeps the normal quality gate independent from external database availability while still allowing DB verification before merge/release.

Other useful quality commands:

```bash
./scripts/build.sh
./scripts/format.sh
./scripts/clean.sh
```

---

# 15. Environment file format

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

The scripts do not `source` the service `.env.local` files as arbitrary shell programs; they parse them as key/value configuration data.

This design also accepts Windows CRLF line endings when files are edited from Windows/WSL workflows.

ASP.NET's `__` convention is used for nested configuration:

```text
Registration__StudentEmailDomain
```

maps to:

```text
Registration:StudentEmailDomain
```

inside `IConfiguration` / `IOptions<T>`.

---

# 16. Secret and configuration handling rules

Never commit or share publicly:

```text
config/env/**/.env.local
service database passwords
MySQL administrator password
private keys
JWT signing keys
GitHub/Jira OAuth secrets
GitHub webhook secrets
future email/provider credentials
storage access/secret keys
```

Safe repository contracts:

```text
config/env/**/.env.example
```

The `.env.example` files must contain placeholders only; do not put actual institutional domains, production OAuth IDs, provider endpoints requiring confidentiality, or real credentials into templates simply because a value is not technically a password.

Additional rules:

1. Give the database administrator password only to people who actually provision the DB server.
2. Normal application processes use service-specific DB accounts, never the MySQL administrator account.
3. Each service DB account receives access only to its own development/test databases.
4. Do not print secrets in scripts, exceptions, responses, or normal application logs.
5. Rotate credentials immediately if a secret enters Git history or another uncontrolled location.
6. Do not reuse development secrets in Test/Production.
7. Do not hard-code configurable business policy in C# or `appsettings.json` simply for convenience.
8. Strongly typed `Options` classes define the configuration contract; ENV supplies the actual runtime values.
9. Required configuration should fail fast during startup rather than silently falling back to insecure or misleading defaults.

### Why ASP.NET User Secrets are not used as the primary project model

ResearchTrack deliberately uses the same environment-key contract across local development, CI/CD, containers, and future hosting platforms. That avoids one configuration model for local development and a different one for deployment.

Local values are kept in gitignored service `.env.local` files; deployment values are injected through the deployment platform's secure environment/secrets capability.

---

# 17. Common command reference

```bash
# First setup
./scripts/setup.sh

# Database provisioning - administrator only
cp config/env/admin/.env.example config/env/admin/.env.local
./scripts/db-init.sh

# Normal DB verification
./scripts/db-status.sh

# EF Core
./scripts/migrate.sh all
./scripts/migrate.sh auth
./scripts/migration-add.sh project AddProjectSchema
./scripts/migration-list.sh project
./scripts/migration-script.sh project

# Development
./scripts/run.sh auth
./scripts/run.sh project
./scripts/run.sh gateway
./scripts/dev.sh core
./scripts/dev.sh integrations
./scripts/dev.sh research
./scripts/dev.sh all
./scripts/health.sh core
./scripts/stop.sh core

# Optional development data seeding
./scripts/seed-dev.sh <service>

# Quality
./scripts/build.sh
./scripts/test.sh
./scripts/test.sh auth
./scripts/test.sh integration
./scripts/check.sh
./scripts/format.sh
./scripts/clean.sh
```

---

# 18. Troubleshooting

## Missing `config/env/<service>/.env.local`

Run:

```bash
./scripts/setup.sh
```

The script creates missing local files from the committed `.env.example` contracts without overwriting existing ones.

## `CHANGE_ME` or placeholder configuration remains

Open the affected service file:

```text
config/env/<service>/.env.local
```

and replace the required placeholders with real local values.

For Story 1, this includes the Auth registration/password policy as well as its DB values.

## Missing `config/env/admin/.env.local`

Only `db-init.sh` requires this file.

If you are the database administrator:

```bash
cp config/env/admin/.env.example config/env/admin/.env.local
chmod 600 config/env/admin/.env.local
```

Then configure administrator credentials.

Normal developers should not create or request this file unless they are responsible for provisioning.

## `CONNECTION FAILED`

Verify the selected service's:

```text
Database__Host
Database__Port
```

and confirm that the MySQL endpoint is reachable from the environment where the script/service is running.

## `AUTH FAILED`

Verify the selected service's:

```text
Database__Username
Database__Password
```

Do not solve this by switching the application to the MySQL administrator account.

## `DATABASE MISSING`

Verify:

```text
Database__Name
Database__TestName
```

The database may not have been provisioned yet, or the configured name may be wrong.

## EF Core cannot create the `DbContext`

Use the repository migration scripts rather than manually invoking `dotnet ef` without the proper environment:

```bash
./scripts/migrate.sh <service>
./scripts/migration-list.sh <service>
```

The selected service's `DbContextFactory` receives configuration from that service's environment through the shared connection-string resolver.

## Service is LIVE but not READY

Check database access:

```bash
./scripts/db-status.sh
```

and inspect:

```text
.run/logs/<service>.log
```

Also verify that service's own `.env.local` contains all required configuration.

## Gateway returns routing/connection errors

Check:

```text
config/env/gateway/.env.local
```

especially:

```text
AUTH_SERVICE_URL
PROJECT_SERVICE_URL
GITHUB_SERVICE_URL
JIRA_SERVICE_URL
MEETING_SERVICE_URL
SUBMISSION_SERVICE_URL
FRONTEND_ORIGIN
```

The gateway route definitions are stable configuration, but destination addresses are environment-owned.

## Registration endpoint fails at startup/configuration validation

Check:

```text
config/env/auth/.env.local
```

and ensure all required `Registration__*`, `PasswordPolicy__*`, and `PasswordHashing__*` values are valid.

Story 1 is intentionally designed to fail fast when required policy is missing instead of silently applying hard-coded business defaults.

---

# 19. Repository configuration principles

The ResearchTrack configuration model is intentionally infrastructure-agnostic and service-owned.

## Code defines contracts; ENV provides values

```text
C# Options / service code
        |
        | defines required keys/types
        v
Environment configuration
        |
        | supplies actual runtime values
        v
Service behavior
```

Example:

```text
UserRole.Student / UserRole.Supervisor
    -> stable domain invariant -> code

Registration__StudentEmailDomain
    -> configurable institutional policy -> ENV
```

## Each service owns its runtime environment

```text
config/env/auth/.env.local
        |
        v
    Auth Service
        |
        v
      Auth DB

config/env/jira/.env.local
        |
        v
    Jira Service
        |
        v
      Jira DB
```

The Auth Service does not need Jira credentials; Jira does not need Auth DB credentials.

## Shared technical helpers do not create shared service state

ResearchTrack may centralize technical infrastructure such as:

```text
API envelopes
exception handling
configuration-binding helpers
DB connection-string construction
health-check conventions
```

but each service still owns its own:

```text
business logic
configuration values
DbContext
migrations
database
integration credentials
service tests
```

## Local, Test and future Production use the same keys

```text
Local
    config/env/<service>/.env.local

Test
    CI/CD / deployment environment variables

Production
    deployment platform secure environment/secrets
```

The application configuration keys remain the same; only the values and delivery mechanism change.

This keeps ResearchTrack flexible without putting environment-specific or configurable business values into the committed codebase.
