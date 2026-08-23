# ResearchTrack development scripts

All local runtime configuration is service-owned under `config/env/<service>/.env.local`.

| Script | Configuration used | Purpose |
|---|---|---|
| `setup.sh` | all committed `.env.example` files | Create missing service `.env.local` files and restore .NET tools/packages |
| `db-init.sh` | `admin/.env.local` + each business service `.env.local` | Provision dev/test DBs and scoped DB users |
| `db-status.sh` | each business service `.env.local` | Verify service dev/test DB access |
| `run.sh <service> [--no-build]` | selected service `.env.local` | Run one component; `--no-build` reuses an existing build |
| `dev.sh <profile> [--no-build]` | delegated to each `run.sh` process | Run a local service profile; `--no-build` avoids concurrent rebuilds |
| `migrate.sh <service|all>` | selected service `.env.local` | Apply EF Core migrations |
| `migration-add.sh` | selected service `.env.local` | Add an EF migration |
| `migration-list.sh` | selected service `.env.local` | List migrations |
| `migration-script.sh` | selected service `.env.local` | Generate migration SQL |
| `test.sh integration` | every business service `.env.local` | Build test DB connection strings and run DB integration tests |
| `health.sh <profile>` | none | Probe liveness/readiness endpoints |

## Setup

```bash
./scripts/setup.sh
```

Then replace `CHANGE_ME` values only in the service files you need.

## Database administrator setup

```bash
cp config/env/admin/.env.example config/env/admin/.env.local
chmod 600 config/env/admin/.env.local
./scripts/db-init.sh
```

Normal developers do not need the admin file after databases and scoped users have been provisioned.

## Profiles

```text
core         auth + project + gateway
integrations auth + project + github + jira + gateway
research     auth + project + meeting + submission + gateway
all          every component
```

The env parser treats files as data (`KEY=value`); it does not execute them as shell scripts.
