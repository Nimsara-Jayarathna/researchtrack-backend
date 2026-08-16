# First-run checklist

From repository root:

- [ ] `dotnet --info` shows an SDK compatible with root `global.json`.
- [ ] MySQL 8+ server is installed and running.
- [ ] `mysql --version` works.
- [ ] `./scripts/setup.sh` completes.
- [ ] `.env.local` exists locally and is not tracked by Git.
- [ ] `./scripts/db-init.sh` completes using a local MySQL admin account.
- [ ] `./scripts/db-status.sh` shows all dev/test databases as `OK`.
- [ ] `./scripts/migrate.sh all` completes (or reports no migrations yet in the initial bootstrap).
- [ ] `./scripts/check.sh` passes.
- [ ] `./scripts/test.sh integration` passes once real-DB integration tests are enabled/configured.
- [ ] `./scripts/dev.sh core` starts Auth, Project, and Gateway.
- [ ] `./scripts/health.sh core` reports healthy processes/readiness.
- [ ] `http://localhost:5000/swagger` is reachable in Development.
- [ ] React is configured to call `http://localhost:5000`.
- [ ] `./scripts/stop.sh core` cleanly stops the local processes.
