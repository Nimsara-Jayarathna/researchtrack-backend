# Troubleshooting

## Service configuration is missing

Run `./scripts/setup.sh`, then edit `config/env/<service>/.env.local`. Any value still set to `CHANGE_ME` must be replaced before that setting is used.

## Database connection fails

Check the selected service file for `Database__Host`, `Database__Port`, `Database__Name`, `Database__Username`, `Database__Password`, `Database__SslMode`, and `Database__AllowPublicKeyRetrieval`. Then run `./scripts/db-status.sh`.

## EF Core cannot create a DbContext

Run migration commands through the repository scripts so the owning service environment is loaded first, for example `./scripts/migrate.sh auth` or `./scripts/migration-add.sh auth AddSomething`.

## Service is LIVE but not READY

The process is running but a readiness dependency such as MySQL is unavailable. Inspect the service log and its own `config/env/<service>/.env.local`.

## Gateway fails at startup

Verify `config/env/gateway/.env.local` contains a valid frontend origin and all downstream service URLs. Gateway destinations are intentionally not hard-coded in `appsettings.json`.
