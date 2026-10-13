# Command reference

Run from the repository root with Bash 4+ and the SDK selected by `global.json`.
Complete [local setup](local-setup.md) and configure the selected service's local
file plus shared JWT before runtime/database commands. Tests and quality tools
have separate requirements in [test strategy](../testing/test-strategy.md).

## Setup, database and migrations

| Command | Purpose and prerequisites |
| --- | --- |
| `./scripts/setup.sh` | Create missing local env files; restore .NET tools and packages. |
| `./scripts/db-init.sh` | Administrator-only provisioning; requires admin and all service DB configuration. |
| `./scripts/db-status.sh` | Check every service's development/test databases using scoped credentials. |
| `./scripts/migrate.sh <service\|all> [--no-build]` | Inspect/apply pending migrations; requires existing databases. |
| `./scripts/migration-add.sh <service> <MigrationName>` | Generate a migration in the owning service. |
| `./scripts/migration-list.sh <service>` | List the selected service's migrations. |
| `./scripts/migration-script.sh <service> [output.sql]` | Generate idempotent SQL; defaults to `artifacts/migrations/<service>.sql`. |

Here and below, `<service>` means a business service (`auth`, `project`, `github`,
`jira`, `meeting`, `submission`), with `gateway` additionally accepted by runtime
and secret-helper commands. The gateway has no business database/migrations.

## Build, run and stop

| Command | Behavior |
| --- | --- |
| `./scripts/build.sh` | Build the solution; uses `CONFIGURATION`, default Debug. |
| `./scripts/start-all.sh` | Check DBs, build, migrate, restart tracked processes and wait for readiness. |
| `./scripts/start-all.sh --provision` | Also provision when DB checks fail; requires admin configuration. |
| `./scripts/start-all.sh --skip-migrations --no-restart --timeout 90` | Example explicit startup options; use only when appropriate for the local state. |
| `./scripts/run.sh <service> [--no-build]` | Run one component in the foreground. |
| `./scripts/dev.sh <profile> [--no-build]` | Build once and launch background services; does not migrate. |
| `./scripts/health.sh <profile>` | Probe service liveness/readiness. |
| `./scripts/stop.sh <profile>` | Stop tracked local processes. |
| `./scripts/seed-dev.sh <service>` | Invoke seeder host; currently exits 2 because no service seeders are registered. |

Profiles: `core` (Auth/Project/Gateway), `integrations` (plus GitHub/Jira),
`research` (Auth/Project/Meeting/Submission/Gateway), `all` (every component).

## Tests and quality

| Command | Behavior |
| --- | --- |
| `./scripts/test.sh all` | Normal suite, excluding database integration tests; default scope is `all`. |
| `./scripts/test.sh <service>` | One component's tests; includes `gateway`. |
| `./scripts/test.sh integration` | Database integration category; requires all service test DBs. |
| `./scripts/coverage.sh [--no-build]` | Test/merge coverage and enforce gates; default configuration Release. |
| `./scripts/test-kafka-integration.sh` | Isolated Docker TLS Kafka/MySQL fixture; see test guide before running. |
| `bash ./scripts/code-quality.sh setup` | Restore separately pinned production C# quality tools. |
| `bash ./scripts/code-quality.sh check [run-id]` | Read-only source check: CSharpier, selected IDE style and production builds. |
| `bash ./scripts/code-quality.sh fix [run-id]` | Deliberately rewrite eligible production source formatting/imports. |
| `bash ./scripts/code-quality.sh analyze [run-id]` | Produce native analyzer/SARIF evidence. |

The older `./scripts/check.sh` runs restore, Release build, tests with coverage,
and whole-solution `dotnet format --verify-no-changes`. `./scripts/format.sh`
rewrites using whole-solution `dotnet format`. Production source now has a separate
CSharpier policy; use the [dedicated quality guide](../testing/code-quality/README.md)
to avoid competing whitespace formatters. Mutation and SonarQube commands are
specified in their linked [testing guides](../testing/README.md).

## Optional User Secrets compatibility helpers

```bash
./scripts/secrets-set.sh auth Configuration:Key
./scripts/secrets-list.sh auth
./scripts/secrets-remove.sh auth Configuration:Key
./scripts/secrets-clear.sh auth
```

The setter prompts for a hidden value; listing masks values. Clear removes that
service's User Secrets store. These helpers do not replace required service ENV
files. See [secrets and configuration](secrets.md).

## Cleanup

`./scripts/clean.sh` removes build/test/coverage artifacts, generated migration SQL,
and `.run` process/log files. Stop tracked services first so their PID files are
not lost while processes are running. Preserve evidence you still need before cleanup.
