# Local setup and running services

All commands run from the backend repository root. For configuration details see
[Secrets and runtime configuration](secrets.md); for database provisioning see
[Database setup](../database/setup.md).

## Prerequisites

1. Install Git and the SDK selected by `global.json` (10.0.300 with compatible
   later .NET 10 feature bands allowed). The file selects the SDK; it does not install it.
2. Use Bash 4+ for repository scripts. macOS's bundled Bash 3.2 does not support
   the case-conversion syntax used here; put an updated Bash on PATH. On Windows,
   WSL is recommended: run Git, .NET, MySQL tools and scripts in the same environment.
3. Make a MySQL 8+ server reachable and install its `mysql` CLI. The scripts do
   not install or start a database server.
4. Install `curl` and allow initial NuGet/tool restore access.

```bash
git --version
dotnet --info
bash --version
mysql --version
curl --version
```

## First-time setup

1. Run `./scripts/setup.sh`. It verifies the SDK family, restores local .NET tools
   and packages, and creates missing `.env.local` files beside committed examples
   for `shared`, `gateway` and all six business services. Existing files are preserved.
2. Configure `config/env/shared/.env.local` with the shared JWT issuer, audience
   and signing key. Then configure each selected service's `.env.local`. Templates
   are contracts, not ready-to-run secrets. See [service settings](service-configuration.md).
3. Provision service databases/users if necessary using [database setup](../database/setup.md).
   Normal developers do not need administrator credentials after provisioning.
4. Apply migrations for the selected services using [migration commands](../database/migrations.md).
5. Start a profile and verify readiness as below.

Setup does not create `admin/.env.local`, provision databases, replace placeholders,
configure providers, or deploy infrastructure. On POSIX filesystems, keep local
configuration permissions at `600`.

## Starting the complete backend

Once all configuration and service databases are ready:

```bash
./scripts/start-all.sh
```

This checks tools/configuration and database access, builds once, applies pending
migrations, restarts tracked processes, starts all seven components, and waits
for readiness. Options:

| Option | Behavior |
| --- | --- |
| `--provision` | If database checks fail, invoke provisioning using the configured administrator file. |
| `--skip-migrations` | Skip schema updates; use only when schemas are already current. |
| `--no-restart` | Preserve already tracked processes instead of restarting them. |
| `--timeout 90` | Override the startup readiness timeout (default 60 seconds). |

## Starting selected services

`dev.sh` builds once and launches a profile in the background. It does not apply
migrations or provision databases; do those first for every selected service.

| Profile | Components |
| --- | --- |
| `core` | Auth, Project, Gateway |
| `integrations` | Auth, Project, GitHub, Jira, Gateway |
| `research` | Auth, Project, Meeting, Submission, Gateway |
| `all` | All six business services and Gateway |

```bash
./scripts/migrate.sh auth
./scripts/migrate.sh project
./scripts/dev.sh core
./scripts/health.sh core
```

Run one component in the foreground with `./scripts/run.sh project`.
Both `run.sh` and `dev.sh` accept `--no-build` when current build output exists.
Do not use it after changing code without rebuilding.

Local ports are Gateway 5000, Auth 5101, Project 5102, GitHub 5103, Jira 5104,
Meeting 5105 and Submission 5106. The frontend normally uses 5173 and calls the
gateway. Configure the gateway destinations and `FRONTEND_ORIGIN` accordingly.

## Health, logs and shutdown

`health.sh` loads the selected components' local configuration, so those files
must exist even for health probes. Each component exposes `/health/live` and `/health/ready`. Liveness proves that
the process responds; readiness checks its registered dependencies. Gateway
readiness does not aggregate downstream services. Inspect individual readiness
responses before assuming the whole application is usable.

```bash
./scripts/health.sh all
curl -i http://localhost:5000/health/ready
curl -i http://localhost:5102/health/ready
./scripts/stop.sh all
```

Background profiles store logs in `.run/logs/<service>.log` and tracked process IDs
in `.run/<service>.pid`. Inspect logs when a health probe fails. Foreground services
stop with Ctrl+C. `stop.sh` accepts the same profiles as `dev.sh`.

## Development seeding

`./scripts/seed-dev.sh <service>` loads the selected service configuration and
invokes the development seeder host. **No service seeder is currently registered**:
the host reports this and exits with code 2. It does not create demo accounts.
Use the real registration flows or the dedicated fixture/account preparation
instructions in the relevant test guide. Future seeders must reuse domain rules.

Next: [Command reference](commands.md), [Swagger](../api/openapi.md),
[Troubleshooting](troubleshooting.md), [First-run checklist](first-run-checklist.md).
