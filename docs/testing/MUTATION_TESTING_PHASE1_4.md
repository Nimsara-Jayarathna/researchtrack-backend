# SE3112 Mutation Testing — Pamudi — Phases 1–4

## Branch

Use:

```text
feature/SE3112-mutation-testing
```

Create it from the branch/revision that already contains Sachith's final unit + mocking suite and the 65% line / 50% branch business coverage gate.

## Why mutation testing comes after unit testing

The normal unit/mock suite answers whether the implementation behaves as expected for the authored examples. Mutation testing measures the strength of those tests by deliberately changing production business logic and checking whether the same tests fail.

Mutation testing therefore **reuses the existing unit-test projects**. We do not create a parallel `MutationTests` test project containing duplicate tests. If a meaningful mutant survives, Pamudi strengthens or adds an ordinary unit test in the existing service test project. That strengthened test then benefits normal CI as well as future mutation runs.

## Phase 1 — repository-local Stryker.NET

Stryker.NET 5.0.0 is pinned in `.config/dotnet-tools.json` so every developer and GitHub Actions can restore the same tool version with:

```bash
dotnet tool restore
```

## Phase 2 — focused baseline configurations

The first baseline intentionally targets deterministic, mutation-friendly business rules that already have strong unit tests.

### Project

Production files:

- `ProjectMilestonePolicy.cs`
- `ProjectRequestValidator.cs`

Tests already live in `tests/ResearchTrack.ProjectService.Tests`.

### Auth

Production file:

- `PasswordPolicyValidator.cs`

Tests already live in `tests/ResearchTrack.AuthService.Tests`.

### Submission

Production files:

- `SubmissionFileRules.cs`
- `SubmissionResponsibilityRules.cs`
- `SubmissionConstants.cs` — includes the `ReviewDecision` workflow rules exercised by `SubmissionWorkflowRulesTests`

Tests already live in `tests/ResearchTrack.SubmissionService.Tests`.

The baseline uses Stryker's `Standard` mutation level. Reports are HTML, JSON, Markdown and console progress.

## Phase 3 — untouched baseline run

Run all three scopes:

```bash
bash ./scripts/mutation-baseline.sh all
```

Or one scope while debugging:

```bash
bash ./scripts/mutation-baseline.sh project
bash ./scripts/mutation-baseline.sh auth
bash ./scripts/mutation-baseline.sh submission
```

Outputs are written below:

```text
artifacts/mutation/
├── project/
├── auth/
├── submission/
└── mutation-summary.md
```

The baseline deliberately uses a Stryker break threshold of `0`. Do **not** fail the build on an arbitrary mutation percentage before measuring the actual codebase.

## Phase 4 — survivor analysis and hardening

For each survivor, classify it before changing tests:

1. **Meaningful test weakness** — add/strengthen a normal unit test in the existing test project.
2. **Equivalent mutant** — document why the observable behavior is unchanged.
3. **Out-of-scope noise** — narrow the mutation scope only when the code is genuinely not part of the business behavior being assessed.

Examples of valuable survivors include boundary changes such as `<` to `<=`, authorization inversion such as `!=` to `==`, logical `&&`/`||` changes, or altered arithmetic/status decisions.

Do not weaken production code and do not write meaningless assertions merely to improve the score.

## Why mutation tests are not placed beside unit tests as a new test type

Stryker is a **test-strength analysis tool**, not a separate xUnit test framework. It temporarily mutates the source project, invokes the existing xUnit tests, and records whether each mutant is detected. Therefore:

```text
Production business code
        ↓ Stryker injects mutant
Existing xUnit unit/mock tests
        ↓
Killed / Survived / No coverage / Timeout
```

Pamudi's new tests, when required, remain in the normal service test projects. They should describe the missing business boundary or behavior, not mention a particular mutant ID.

## What comes after these phases

After the baseline is reviewed:

- strengthen tests for meaningful survivors;
- rerun and calculate the improved mutation score;
- expand mutation scope selectively to critical orchestration (Auth, Project, Submission, GitHub, Jira and Gateway);
- choose a defensible regression threshold from the verified score;
- make the separate manual mutation workflow enforce that threshold;
- package the HTML/JSON/Markdown reports as Pamudi's final assessment evidence.
