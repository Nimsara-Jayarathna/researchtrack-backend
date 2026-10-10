# Phase 2 compiler/test-run fixes

This revision fixes issues reported by the real .NET 10 build/test run.

## Fixed

- xUnit1051 in Jira webhook mock tests by using `TestContext.Current.CancellationToken` for async calls and interaction verification.
- xUnit1051 in Meeting mock tests by using the xUnit test cancellation token consistently.
- xUnit1051 in GitHub reconciliation mock tests by using the xUnit test cancellation token consistently.
- `CS0118` in Project mock tests with an explicit alias for `ResearchTrack.ProjectService.Features.Projects.ProjectService`.
- `CS4014` in Project mock tests by awaiting `CreateDbContextAsync` interaction verification.
- Replaced `DidNotReceiveWithAnyArgs(... default ...)` patterns on cancellable async calls with explicit NSubstitute argument matchers plus the xUnit cancellation token.
- Documentation now separates the unit/mock suite from `Category=DatabaseIntegration` tests.

## Correct verification commands

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration"
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration" --collect:"XPlat Code Coverage"
```

Equivalent repository script:

```bash
./scripts/test.sh all
```

Database integration tests are separate and require configured test databases:

```bash
./scripts/db-init.sh
./scripts/test.sh integration
```

## Environment limitation

The package was statically rechecked in the artifact environment, but that environment does not contain the .NET SDK. The authoritative compile/test verification must therefore be performed on the project's .NET 10 workstation or CI runner using the commands above.
