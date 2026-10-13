# First-run checklist

- [ ] .NET SDK matches repository policy.
- [ ] `./scripts/setup.sh` completed successfully.
- [ ] Shared JWT values are configured in `config/env/shared/.env.local`.
- [ ] Required `config/env/<service>/.env.local` files exist locally and are not tracked by Git.
- [ ] Every `CHANGE_ME` used by the services you run has been replaced.
- [ ] MySQL is reachable.
- [ ] Service-scoped development/test databases exist.
- [ ] EF Core migrations have been applied.
- [ ] Required services report healthy liveness/readiness endpoints.
- [ ] Normal tests pass before opening a pull request.

Follow [local setup](local-setup.md) for the ordered procedure and [database setup](../database/setup.md) for administrator-only provisioning. Configure and migrate every service in the selected profile before running it. Full-stack database checks require all six service configurations.
