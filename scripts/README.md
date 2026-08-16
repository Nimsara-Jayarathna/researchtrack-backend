# ResearchTrack development scripts

Run all scripts from the repository root. Bash is the supported scripted workflow on Linux/macOS; WSL is recommended on Windows.

## Configuration sources

| Source | Purpose | Who needs it? |
|---|---|---|
| `.env.local` | Normal development/database configuration | All backend developers |
| `.env.admin.local` | MySQL provisioning administrator credentials | Database administrator only |
| ASP.NET User Secrets | Feature/application secrets | Developer working on the relevant feature |

The scripts parse `.env.local`/`.env.admin.local` as simple `KEY=value` data. They are not sourced as executable Bash.

## Script reference

| Script | Configuration used | Purpose |
|---|---|---|
| `setup.sh` | creates/validates `.env.local` | Validate prerequisites, restore tools/packages |
| `db-init.sh` | `.env.local` + `.env.admin.local` | Provision dev/test DBs and scoped service users |
| `db-status.sh` | `.env.local` | Verify every service dev/test DB |
| `run.sh <service>` | `.env.local` | Run one ASP.NET project |
| `dev.sh <profile>` | `.env.local` via `run.sh` | Run multiple ASP.NET projects in background |
| `stop.sh <profile>` | none | Stop background processes |
| `health.sh <profile>` | none | Check `/health/live` and `/health/ready` |
| `migrate.sh <service|all>` | `.env.local` | Apply EF Core migrations |
| `migration-add.sh <service> <name>` | `.env.local` | Add migration to owning service |
| `migration-list.sh <service>` | `.env.local` | List migrations |
| `migration-script.sh <service>` | `.env.local` | Generate idempotent migration SQL |
| `test.sh [scope]` | only integration scope uses `.env.local` | Run tests/coverage |
| `check.sh` | none | Pre-PR restore/build/test/format gate |
| `build.sh` | none | Build solution |
| `format.sh` | none | Apply formatting |
| `clean.sh` | none | Remove build/test artifacts |
| `seed-dev.sh <service>` | `.env.local` | Reserved development seeding entry point |
| `secrets-set.sh` | ASP.NET User Secrets | Store feature secret |
| `secrets-list.sh` | ASP.NET User Secrets | List secret keys with masked values |
| `secrets-remove.sh` | ASP.NET User Secrets | Remove one secret |
| `secrets-clear.sh` | ASP.NET User Secrets | Clear all project User Secrets |

## Profiles

Supported by `dev.sh`, `stop.sh`, and `health.sh`:

```text
core
integrations
research
all
```

## Normal developer workflow

```bash
./scripts/setup.sh
# configure .env.local
./scripts/db-status.sh
./scripts/migrate.sh all
./scripts/dev.sh core
./scripts/health.sh core
```

## Database administrator workflow

```bash
cp .env.admin.example .env.admin.local
chmod 600 .env.admin.local
# configure .env.admin.local and .env.local
./scripts/db-init.sh
./scripts/db-status.sh
```

Normal developers do not need `.env.admin.local`.

## Database configuration

All database-aware scripts use:

```text
MYSQL_HOST
MYSQL_PORT
```

plus the selected service's:

```text
<SERVICE>_DB_NAME
<SERVICE>_TEST_DB_NAME
<SERVICE>_DB_USER
<SERVICE>_DB_PASSWORD
```

No script hard-codes a database server address or administrator credentials.

## Safety rules

- `.env.local` and `.env.admin.local` are gitignored.
- Keep both files permission `600` where supported.
- `db-init.sh` is the only script that loads `.env.admin.local`.
- Service scripts never use `MYSQL_ADMIN_USER`/`MYSQL_ADMIN_PASSWORD`.
- Database integration tests use only the `*_TEST_DB_NAME` values.
- `check.sh` does not require database availability.
- User Secrets are used for future application/feature secrets rather than adding everything to `.env.local`.
