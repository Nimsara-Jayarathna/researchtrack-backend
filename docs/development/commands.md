# Command reference

All examples run from repository root.

```bash
./scripts/setup.sh
./scripts/db-init.sh
./scripts/db-status.sh
./scripts/migrate.sh all
./scripts/dev.sh core
./scripts/health.sh core
./scripts/check.sh
./scripts/stop.sh core
```

## Run one service

```bash
./scripts/run.sh gateway
./scripts/run.sh auth
./scripts/run.sh project
./scripts/run.sh github
./scripts/run.sh jira
./scripts/run.sh meeting
./scripts/run.sh submission
```

## Tests

```bash
./scripts/test.sh
./scripts/test.sh auth
./scripts/test.sh integration
```

## Service environment configuration

```bash
# Creates missing service-local files without overwriting existing values
./scripts/setup.sh

# Edit only the service you are working on
${EDITOR:-vi} config/env/auth/.env.local

# DB administrators provision credentials from the separate admin contract
cp config/env/admin/.env.example config/env/admin/.env.local
./scripts/db-init.sh
```

Actual local values live only in gitignored `config/env/<service>/.env.local` files. Test/deployment values are injected by the CI/CD or hosting environment.
