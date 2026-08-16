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

## Secrets

```bash
./scripts/secrets-set.sh auth "Jwt:SigningKey"
./scripts/secrets-list.sh auth
./scripts/secrets-remove.sh auth "Jwt:SigningKey"
```
