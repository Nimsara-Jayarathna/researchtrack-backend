# Troubleshooting

Run commands from the repository root. Start with [local setup](local-setup.md),
[configuration](secrets.md), and the selected service's `.env.example`. Inspect
`.run/logs/<service>.log` for background processes. Include trace IDs in bug reports,
not credentials, complete environment files, access tokens or private keys.

## Missing local files or invalid placeholders

Run `./scripts/setup.sh`, then configure shared JWT and the required service files.
Setup preserves existing values and does not fill placeholders. A missing admin
file blocks provisioning only; ordinary startup/migration uses scoped service
configuration. Invalid required policy is intentionally rejected at startup.

If Bash reports `bad substitution`, check `bash --version`: the scripts use Bash
4+ syntax. Install/use an updated Bash on macOS or run the documented WSL environment
on Windows. If the SDK cannot be selected, compare installed SDKs with `global.json`.

## Database diagnostics

Run `./scripts/db-status.sh` after configuring every service's development/test
settings. This checks all services, not only the one currently running.

| Failure | Action |
| --- | --- |
| `CONNECTION FAILED` | Verify server is running, host/port are reachable, firewall and TLS settings match. |
| `AUTH FAILED` | Verify scoped username/password and grants; keep administrator credentials out of application files. |
| `DATABASE MISSING` | Check configured name and provisioning; use the administrator-only setup if needed. |
| Integration test refuses connection | Use a dedicated database name satisfying `researchtrack_test_`; never bypass the guard for live data. |

See [database setup](../database/setup.md) for ownership and administrator steps.
Keep an explicit connection-string override consistent with intended `Database__*`
values; an obsolete populated override can send the application to a different DB.

## EF Core cannot create a context or inspect migrations

Restore tools with setup, configure the selected service, and use repository
migration scripts rather than an ad hoc `dotnet ef` command:

```bash
./scripts/migration-list.sh project
./scripts/migrate.sh project
```

Design-time factories use non-routable placeholders; the migration script supplies
the validated runtime connection when applying/inspecting. Do not interpret an EF
error as no pending migrations. See [migrations](../database/migrations.md).

## Process is live but not ready

Check its `/health/ready` response, database access and service log. `/health/live`
only proves process responsiveness. Gateway readiness does not check every business
service. Verify individual services with `./scripts/health.sh <profile>`.
Profiles launch in the background, so also inspect logs after startup; a profile
launch message alone does not prove readiness.

## Gateway startup, routing, CORS or authentication fails

1. Verify gateway `FRONTEND_ORIGIN` and all six `*_SERVICE_URL` destinations.
2. Verify the target service runs on the configured scheme/host/port; local ports
   differ from container port 8080. A 502 usually indicates an upstream failure.
3. Match the allowed origin to the browser's actual frontend origin for CORS.
4. For 401/403, inspect session expiry, shared JWT issuer/audience/key, secure-cookie
   deployment settings and the caller's role/resource access. Do not treat an
   authorization failure as a reason to loosen project membership checks.

## Swagger document returns 404 or empty paths

- Empty gateway `paths`: select a business service from the UI; documents are independent.
- `/swagger/<service>/v1/swagger.json` returns 404: confirm the deployed gateway
  includes service routes and OpenAPI is enabled on both gateway and that service.
- Unknown service/version/path: only the six exact GET v1 document routes are exposed.
- 502/503: investigate downstream availability/readiness and cluster destinations.
- UI opens but a document fails: check that specific service; other documents may still work.

After changing environment bundles, redeploy/restart affected components through
the environment's workflow. See [OpenAPI setup](../api/openapi.md). A configured
Production hostname does not prove that a particular image/configuration is live.

## Registration, email or integration configuration fails

For Auth, verify the complete `Registration__*`, `PasswordPolicy__*`,
`PasswordHashing__*`, Brevo and session/cookie contract. For GitHub, verify App
credentials/key material, callback URLs, webhook secret and repository limits.
For Jira, verify OAuth credentials, redirect URI and encryption key. For Submission,
verify storage credentials/bucket and upload policies. See
[service configuration](service-configuration.md) and the feature's guide.

## Seeding or quality checks behave differently than expected

The development seeder host has no registered seeders and exits 2; it does not
create accounts. Production source formatting uses CSharpier; a whole-solution
`dotnet format` check is a different tool path. Use the [quality guide](../testing/code-quality/README.md)
to identify the correct formatter and evidence before rewriting source.

## Production is unreachable

Production may be intentionally stopped for cost control. Inspect the documented
runtime state/workflows before treating this as an outage or changing infrastructure.
Use the [Azure runbook](../devops/azure-production/RUNBOOK.md) and
[runtime state-preservation guide](../devops/azure-runtime/README.md). Do not run
local provisioning/migration scripts against Production to diagnose availability.
