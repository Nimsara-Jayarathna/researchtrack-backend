# ResearchTrack Backend Tests

The backend uses service-specific xUnit projects. Keep tests close to the service boundary they validate.

## Test categories by intent

- **Pure unit tests:** deterministic rules, parsers, validation, calculations, authorization decisions and state transitions.
- **Mocked unit tests (next phase):** service orchestration with isolated dependencies and interaction verification.
- **Integration tests:** ASP.NET/EF/infrastructure boundaries using the existing ResearchTrack test infrastructure.
- **Performance tests:** k6 under `tests/performance`; separate from this unit-testing workstream.

## Engineering conventions

- No live Jira/GitHub/S3/Azure/MySQL calls from pure unit tests.
- Prefer exact assertions over broad `NotNull` assertions.
- Exercise boundary values on both sides of comparisons.
- Assert state that must remain unchanged after failures.
- Use `[Theory]` for equivalence classes and boundaries.
- Avoid testing trivial getters/setters or framework code.
- Add a mock only when an interaction is part of the expected behaviour.

See `docs/testing/unit/UNIT_TESTING_STRATEGY.md` and `docs/testing/coverage/UNIT_TEST_COVERAGE_MATRIX.md` for the full plan.
