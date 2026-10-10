# SE3112 Unit Testing — Phase 1 Implementation Summary

## Scope delivered

This revision strengthens the existing xUnit test architecture without adding a mocking framework yet. It is intentionally structured so Phase 2 mocking and Phase 3 mutation testing can build on a clean deterministic unit layer.

### Added production testability seam

- `src/Services/ResearchTrack.ProjectService/Properties/AssemblyInfo.cs`
  - grants `ResearchTrack.ProjectService.Tests` access to internal business policies through `InternalsVisibleTo`
  - does not change runtime business behaviour

### Added ProjectService unit tests

- `ProjectMilestonePolicyTests.cs`
- `ProjectRequestValidatorTests.cs`

These target normalization, validation, student/leader rules, milestone chronology, status transitions, progress calculations and aggregate dates.

### Added AuthService unit tests

- `PasswordPolicyValidatorBoundaryTests.cs`
- `PasswordPolicyOptionsTests.cs`

These target exact boundaries, character-class rules, disabled requirements, argument guards and configuration validation.

### Added SubmissionService unit tests

- `SubmissionFileRulesBoundaryTests.cs`
- `SubmissionResponsibilityRulesBoundaryTests.cs`

These target file extension/MIME normalization, deterministic allowed-type handling, safe file names, responsibility modes, stale assignment handling and exact authorization/conflict distinctions.

### Existing test suites preserved

The uploaded backend already contains substantial GitHub, Jira, Meeting, Gateway and integration test coverage. This revision does not duplicate those suites. Existing tests remain in place unchanged except for the new complementary files above.

## Test design quality

The added tests emphasize:

- exact boundary values
- positive and negative paths
- parameterized equivalence classes
- exact error fields/status codes
- immutable/terminal state rules
- deterministic aggregate calculations
- state preservation semantics
- no live external dependencies

This design is intended to kill common Stryker mutations later rather than merely inflate line coverage.

## Important verification note

This workspace does not provide the .NET SDK/runtime, so `dotnet build` and `dotnet test` could not be executed here. The implementation has been structurally reviewed, but the repository should be validated in the normal .NET 10 development/CI environment before merge:

```bash
dotnet restore ResearchTrack.sln
dotnet build ResearchTrack.sln -c Release --no-restore
dotnet test ResearchTrack.sln -c Release --no-build --filter "Category!=DatabaseIntegration"
```

Then collect measured coverage:

```bash
dotnet test ResearchTrack.sln -c Release --filter "Category!=DatabaseIntegration" --collect:"XPlat Code Coverage"
```

Do not claim a final coverage percentage until this runtime report exists.

## Next phase

After this suite is green, add one mocking framework centrally and cover service orchestration/dependency interactions. See:

- `docs/testing/unit/UNIT_TESTING_STRATEGY.md`
- `docs/testing/coverage/UNIT_TEST_COVERAGE_MATRIX.md`
- `tests/UNIT_TESTING_README.md`
