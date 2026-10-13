# SonarQube Cloud — Kafka new-code issues (2026-10-13)

## Observed result

The completed SonarQube Cloud analysis for merge/push `4cd749e` reached the compute engine successfully, but the quality gate failed because **new-code security rating was B (2), while the required rating was A (1)**. The uploaded screenshots identified **9 open new-code issues**:

- S6444 (security): regex matching without an explicit timeout, `KafkaRuntimeOptions.cs` at the original lines 52, 57 and 106 (3).
- Roslyn analyzer performance findings: evaluate logging arguments only if the logger is enabled, in `KafkaClients.cs` (2) and `KafkaMessagingWorkers.cs` (1).
- Use source-generated regular expressions in `KafkaRuntimeOptions.cs` (3).

Overall coverage, new-code coverage, duplication and hotspot review had already passed their respective gates.

## Implementation

- `KafkaRuntimeOptions` is now a partial class with four source-generated `[GeneratedRegex]` matchers: topic, contract version placeholder, broker endpoint, and CA PEM format. Each matcher has an explicit **1-second** timeout. The broker endpoint regex was proactively included although only the other three were flagged in the screenshot. Existing patterns and validation ordering were preserved.
- Kafka native client debug callbacks now check `ILogger.IsEnabled(LogLevel.Debug)` before reading the Kafka native log-entry details.
- The durable consumer offset-commit information log now checks `ILogger.IsEnabled(LogLevel.Information)` before reading partition and offset values.
- Added invalid topic, placeholder contract version, and malformed bootstrap endpoint regression cases to the existing `KafkaRuntimeOptionsTests` project. Existing CA and environment-binding tests remain intact.

## Deliberately unchanged

- Kafka SSL authentication, CA installation directory and CA delivery process.
- Kafka topic names, connection configuration, group IDs, endpoints and serialization contracts.
- Producer/consumer behavior, outbox publishing, offset commits, retries and health statuses.
- Sonar project quality-gate thresholds and `SONAR_ENFORCE_GATE`.

The `S5443` warning on the publicly writable `/tmp` certificate location appears in the longer scanner log but **was not present in the 9 open *new-code* issues shown in the screenshot**. It is separate from this targeted new-code quality-gate fix. Any later change to the CA location must be coordinated with production deployment/environment configuration and filesystem permissions.

## Validation commands

```bash
# Local .NET 10 environment

dotnet build ResearchTrack.sln -c Release

dotnet test tests/ResearchTrack.GitHubService.Tests/ResearchTrack.GitHubService.Tests.csproj \
  -c Release --filter 'Category!=DatabaseIntegration'

dotnet test tests/ResearchTrack.JiraService.Tests/ResearchTrack.JiraService.Tests.csproj \
  -c Release --filter 'Category!=DatabaseIntegration'

bash ./scripts/code-quality.sh check sonar-kafka-fix
```

On PR/merge, rerun **Backend SonarQube Cloud Analysis**, then verify that `new_security_rating` is A, `new_coverage >= 80`, and the new-code issues list is clear. Only SonarQube's completed result can confirm the gate. No local .NET SDK/SonarCloud runtime is available in the authoring environment; these checks have **not** been claimed as executed.
