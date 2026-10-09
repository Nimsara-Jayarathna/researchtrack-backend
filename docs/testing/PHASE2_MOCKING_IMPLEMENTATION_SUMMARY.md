# SE3112 Phase 2 Mocking Implementation Summary

This branch extends the Phase 1 deterministic unit suite with NSubstitute-based interaction tests.

## Added

- Central `NSubstitute` package version and test-project references.
- EF Core InMemory references only for test projects that need deterministic contexts for direct `IDbContextFactory<T>` services.
- Mock-based tests across Auth, Project, Submission, Meeting, Jira and GitHub services.
- Positive interaction checks (`Received(1)`).
- Negative interaction checks (`DidNotReceive`).
- External dependency failure tests.
- Duplicate/replay suppression tests.
- Persistence-side-effect checks.
- `docs/testing/MOCKING_STRATEGY.md`.

## Mutation-testing readiness

The suite now targets branch and interaction mutants such as:

- authorization branch inversion;
- skipped dependency calls;
- duplicate call mutations;
- removed side-effect prevention;
- failure path removal;
- replay/duplicate handling;
- external-service exception handling;
- state changes following dependency failure.

## Required verification in a .NET SDK environment

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
dotnet test ResearchTrack.sln -c Release --no-build
dotnet test ResearchTrack.sln -c Release --collect:"XPlat Code Coverage"
```

The generation environment used for this package does not contain the .NET SDK, so pass/fail counts are intentionally not fabricated. Run the commands above locally or in GitHub Actions before merge.
