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

Docker/Testcontainers/Prometheus packages are intentionally absent from this current development baseline.
