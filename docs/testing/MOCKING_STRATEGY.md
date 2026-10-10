# SE3112 Unit Testing Phase 2 — Mocking Strategy

Phase 2 adds interaction-oriented unit tests with NSubstitute while preserving the Phase 1 rule/boundary tests.

## Engineering principles

- Mock only architectural boundaries (authorization, user-directory, token, storage, external API, queue and repository contracts).
- Keep business entities and rule classes real.
- Use EF Core InMemory only as a deterministic persistence seam where production services directly depend on `IDbContextFactory<TContext>`.
- Verify both positive interactions (`Received(1)`) and side-effect prevention (`DidNotReceive`).
- Cover external failures and duplicate/replay paths because these are high-value mutation-testing targets.
- Do not mock DTOs, value objects, static rule helpers, or trivial properties.

## Phase 2 targets implemented

| Service area | Interaction coverage |
|---|---|
| Auth | password verification, timing guard, access-token creation, refresh-token persistence prevention |
| Project | current-user authorization, student resolution, no-persistence on forbidden/unresolved users |
| Submission | project context, user profile lookup, object-storage grant creation, storage failure state |
| Meeting | authorization branch selection, persistence call counts, invalid-role and invalid-channel no-side-effect behavior |
| Jira | webhook authentication, durable event handling, scheduler call count, replay suppression |
| GitHub | installation token lookup, repository/head checks, reconciliation queueing, unchanged/failure no-queue behavior |

These tests intentionally complement rather than replace existing integration tests.
