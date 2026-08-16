# Troubleshooting

## `dotnet` SDK mismatch

Run `dotnet --list-sdks`. The repository baseline is .NET SDK 10.0.300 and `global.json` permits compatible later .NET 10 feature bands.

## MySQL connection fails

1. Confirm the MySQL server is running.
2. Check `MYSQL_HOST`/`MYSQL_PORT` in `.env.local`.
3. Run `./scripts/db-init.sh` if databases/users have not been created.
4. Run `./scripts/db-status.sh`.

## Service starts but `/health/ready` fails

The process can be live while its DB is unavailable. Check the relevant service DB entry in `./scripts/db-status.sh` and ensure migrations/configuration are correct.

## Port already in use

Ports are fixed in the development baseline. Stop the conflicting process or run `./scripts/stop.sh all` if it was started by `dev.sh`.

## Background service fails

Inspect `.run/logs/<service>.log`.

## Database integration tests fail immediately

Run `./scripts/db-init.sh` and `./scripts/db-status.sh`, then invoke integration tests only through `./scripts/test.sh integration`. The test helper rejects connection strings not targeting `researchtrack_test_*` databases.
