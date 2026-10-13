# Dependency baseline

NuGet package versions are centralized in `Directory.Packages.props`.

Core packages include:

- `Microsoft.EntityFrameworkCore` / `Microsoft.EntityFrameworkCore.Design` 10.0.9
- `MySql.EntityFrameworkCore` 10.0.9
- `Yarp.ReverseProxy` 2.3.0
- `Swashbuckle.AspNetCore` 10.2.3
- `Microsoft.AspNetCore.Mvc.Testing` 10.0.9
- xUnit v3 / Microsoft.NET.Test.Sdk / coverlet

Do not use floating versions for core dependencies. Package upgrades should be explicit pull requests and should run the full quality/integration checks.

The backend also uses `Confluent.Kafka`, `prometheus-net.AspNetCore`, `AWSSDK.S3`,
JWT bearer authentication, and NSubstitute; consult the central package file for
current versions. Docker/Compose runtime infrastructure and isolated integration
fixtures live under `deploy/` and `tests/Kafka/`, rather than being inferred from
NuGet package presence. Prometheus/Grafana deployment is implemented.

The SDK is selected by [global.json](../../global.json). Tools for EF, coverage and
mutation are pinned in [.config/dotnet-tools.json](../../.config/dotnet-tools.json);
production formatting has a separate manifest described in the
[code-quality guide](../testing/code-quality/README.md).
