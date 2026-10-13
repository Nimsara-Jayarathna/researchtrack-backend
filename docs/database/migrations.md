# EF Core migrations

Every service owns its `DbContext`, schema and migration history. Complete
[local configuration and database provisioning](setup.md) before applying changes.
Run commands from the repository root after `./scripts/setup.sh` restores tools.

| Service | Context |
| --- | --- |
| Auth | `AuthDbContext` |
| Project | `ProjectDbContext` |
| GitHub | `GitHubDbContext` |
| Jira | `JiraDbContext` |
| Meeting | `MeetingDbContext` |
| Submission | `SubmissionDbContext` |

## Apply or inspect migrations

```bash
./scripts/migration-list.sh project
./scripts/migrate.sh project
# Apply each business service's pending migrations:
./scripts/migrate.sh all
```

`migrate.sh` builds the selected project (or whole solution for `all`), checks
migration state, skips updates when none are pending, and applies only pending
migrations. An inspection error fails the command; it is not treated as an
up-to-date database. It uses the configured runtime connection explicitly because
design-time factories deliberately use non-routable placeholders.

After an existing build, `./scripts/migrate.sh all --no-build` avoids rebuilding.
The build configuration comes from `CONFIGURATION` (default Debug). Use a matching
build when selecting `--no-build`.

## Add a schema change

1. Update the owning service's entities/configuration.
2. Generate a migration:

```bash
./scripts/migration-add.sh project AddProjectSchema
```

3. Review the generated migration and model snapshot under the service's
   `Persistence/Migrations/`; commit them with the feature.
4. Apply it to the development database and verify the affected behavior and
   provider-specific constraints using MySQL integration tests.

Never ignore migration folders or introduce cross-service foreign keys/navigation
properties. Store stable external identifiers and call the owning service API.

## Generate reviewable SQL

```bash
./scripts/migration-script.sh project
./scripts/migration-script.sh project artifacts/migrations/project-review.sql
```

This generates an idempotent SQL script. The default output is
`artifacts/migrations/<service>.sql`; it does not apply the SQL. Review target
connection, schema changes and any data-loss operations before deployment.
Production migration execution belongs to the deployment workflow/runbook.
