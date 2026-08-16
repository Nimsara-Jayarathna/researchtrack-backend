# EF Core migrations

Each ResearchTrack microservice owns its schema, `DbContext`, and migration history.

- Auth -> `AuthDbContext`
- Project -> `ProjectDbContext`
- GitHub -> `GitHubDbContext`
- Jira -> `JiraDbContext`
- Meeting -> `MeetingDbContext`
- Submission -> `SubmissionDbContext`

`db-init.sh` creates only logical local development/test databases and scoped users. Application tables are created by service-owned EF migrations.

## Commands

```bash
./scripts/migration-add.sh project AddProjectSchema
./scripts/migration-list.sh project
./scripts/migrate.sh project
./scripts/migrate.sh all
./scripts/migration-script.sh project
```

Migration source files must be committed to Git. Never add service `Migrations/` folders to `.gitignore`.

Do not create cross-service foreign keys or EF navigation properties. Store stable identifiers and communicate across service APIs when a service needs information owned elsewhere.
