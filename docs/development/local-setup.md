# Local setup

1. Install the .NET SDK required by `global.json`, Git, Bash/WSL, and a MySQL 8+ compatible client/server.
2. Run `./scripts/setup.sh`.
3. Edit `config/env/<service>/.env.local` for each service you plan to run.
4. If provisioning is required, create `config/env/admin/.env.local` from its example and run `./scripts/db-init.sh`.
5. Run `./scripts/db-status.sh`.
6. Apply migrations with `./scripts/migrate.sh all`.
7. Start a profile, for example `./scripts/dev.sh core`.
8. Check `./scripts/health.sh core`.
