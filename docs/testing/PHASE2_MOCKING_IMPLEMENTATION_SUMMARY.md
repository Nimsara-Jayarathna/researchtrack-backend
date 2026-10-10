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
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration"
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration" --collect:"XPlat Code Coverage"
```

The generation environment used for this package does not contain the .NET SDK, so pass/fail counts are intentionally not fabricated. Run the commands above locally or in GitHub Actions before merge.

## Verification note: unit vs database integration tests

The solution intentionally contains database integration tests tagged with `Category=DatabaseIntegration`.
Those tests require the `RESEARCHTRACK_TEST_*_CONNECTION` variables and initialized test databases.
They are not part of the isolated Unit Testing & Mocking evidence run.

Use either of these for the unit/mock suite:

```bash
./scripts/test.sh all
```

or:

```bash
dotnet test ResearchTrack.sln -c Release --filter "Category!=DatabaseIntegration" --collect:"XPlat Code Coverage"
```

Run database integration tests separately after database initialization:

```bash
./scripts/db-init.sh
./scripts/test.sh integration
```
