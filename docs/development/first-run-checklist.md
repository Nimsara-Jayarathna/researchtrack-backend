# First-run checklist

- [ ] .NET SDK matches repository policy.
- [ ] `./scripts/setup.sh` completed successfully.
- [ ] Required `config/env/<service>/.env.local` files exist locally and are not tracked by Git.
- [ ] Every `CHANGE_ME` used by the services you run has been replaced.
- [ ] MySQL is reachable.
- [ ] Service-scoped development/test databases exist.
- [ ] EF Core migrations have been applied.
- [ ] Required services report healthy liveness/readiness endpoints.
- [ ] Normal tests pass before opening a pull request.
