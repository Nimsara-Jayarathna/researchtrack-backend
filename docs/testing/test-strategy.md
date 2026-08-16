# Test strategy

ResearchTrack uses xUnit v3, ASP.NET `WebApplicationFactory`, coverlet, and real MySQL for database integration coverage.

## Default baseline tests

```bash
./scripts/test.sh
```

The default suite excludes the `DatabaseIntegration` category so developers and CI can validate service startup, liveness, OpenAPI, standardized errors, and trace IDs without requiring a database instance.

## MySQL database integration tests

Initialize local test databases once:

```bash
./scripts/db-init.sh
./scripts/db-status.sh
```

Then:

```bash
./scripts/test.sh integration
```

The script exports test connection strings only for `researchtrack_test_*` databases. `TestDatabaseConfiguration` refuses a connection string that doesn't include that test database naming convention.

Do not use EF InMemory as a substitute for MySQL provider behavior. Feature teams should add real database integration tests for migrations, constraints, queries, transactions, and provider-specific behavior where relevant.
