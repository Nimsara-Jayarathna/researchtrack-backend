# Test strategy and verification

ResearchTrack uses xUnit v3, ASP.NET `WebApplicationFactory`, mocks, Coverlet and
MySQL integration tests. Run all commands from the repository root with the SDK
selected by `global.json`; restore tools/packages before the first run.

## Normal tests

```bash
./scripts/setup.sh
./scripts/test.sh all
# Target one component instead:
./scripts/test.sh gateway
./scripts/test.sh auth
```

`test.sh` defaults to `all`, uses `CONFIGURATION` (default Debug), collects coverage
and excludes `DatabaseIntegration`. Shared factories use nonconnecting database
placeholders and deterministic test settings, including OpenAPI enabled and Kafka
disabled. Production secrets and real local brokers are not inherited by those hosts.
Normal tests cover service startup, health, OpenAPI, standardized errors, business
rules and mocked dependencies; they do not certify a live production environment.

The default script filter does not explicitly exclude `KafkaIntegration`. For a
run that excludes both external integration categories use:

```bash
dotnet test ResearchTrack.sln --filter 'Category!=DatabaseIntegration&Category!=KafkaIntegration'
```

## MySQL integration tests

1. Follow [database setup](../database/setup.md), including service-scoped test
   databases. Only an administrator needs to run provisioning.
2. Check the configured development/test connections:

```bash
./scripts/db-status.sh
```

3. Run:

```bash
./scripts/test.sh integration
```

The script builds and exports each service's test connection string, then selects
`Category=DatabaseIntegration`. Test configuration refuses database names that
lack the `researchtrack_test_` guard. Never point tests at development or Production
data. Use real MySQL for migrations, unique constraints, provider queries and
transaction behavior; EF InMemory is not a substitute for those checks.

## Kafka integration tests

`./scripts/test-kafka-integration.sh` creates an isolated local Docker TLS
Kafka/MySQL fixture. It requires Docker/Compose, .NET, OpenSSL and Python 3, a local
Docker socket and free fixture ports. It generates synthetic configuration rather
than reading production bundles. It stops its own containers on exit and retains
fixture data/evidence; follow the [Kafka flow guide](../devops/azure-production/KAFKA_TOPICS_EVENT_FLOWS.md)
and workflow instructions before running or cleaning those artifacts.

## Coverage and pre-PR checks

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
./scripts/coverage.sh --no-build
bash ./scripts/code-quality.sh setup
bash ./scripts/code-quality.sh check
```

Coverage uses Release by default, runs the non-database suite and produces merged
all-source and business-scope reports under `artifacts/coverage/`. The business
scope and thresholds are defined by the coverage script/workflow, not by test
counts. See [coverage/CI evidence](coverage/COVERAGE_AND_CI.md),
[hardening strategy](coverage/PHASE4_BUSINESS_COVERAGE_HARDENING.md) and
[gap register](coverage/COVERAGE_GAP_REGISTER.md).

Production C# uses CSharpier and selected Roslyn style rules. The dedicated
[quality guide](code-quality/README.md) explains exclusions, analyzer evidence
and the separate SonarQube Cloud workflow. Older `check.sh`/`format.sh` commands
still use whole-solution `dotnet format`; do not assume they are interchangeable
with the production CSharpier gate.

## Feature-focused verification

Registration tests should cover configured policy, normalization, aliases,
duplicate prevention, password policy/hashing, OTP expiry/reuse, registration
session consumption and role inference/selection. Login tests cover generic bad
credentials, cookie attributes, JWT validation, refresh rotation/replay, logout
and resource authorization. See [registration](../stories/registration.md) and
[session behavior](../development/us-102-secure-login-rbac.md).

Gateway Swagger tests cover six-cluster forwarding/path rewriting, UI selection,
retained gateway JSON, disabled OpenAPI, unknown paths/versions, unsupported
methods and downstream errors. These tests use controlled HTTP responses and do
not require the real services or Production network.

## Additional evidence

[Mutation testing](mutation/README.md) evaluates test sensitivity;
[authenticated k6 performance testing](../../tests/performance/README.md) measures
specified live scenarios. They have separate prerequisites and workflows and do
not replace normal tests. Performance runtime sessions restore prior Azure state;
follow their operational instructions rather than manually waking Production.

Keep assessment/implementation evidence in its topic directory and publish only
results from executed checks. Passing unit tests or a compile does not establish
provider integration, responsive frontend behavior or production readiness.
