# SE3112 Mutation Testing — Phase 1 Dedicated Harness

## Why this exists

The main ResearchTrack test projects use xUnit v3. Stryker.NET 5.0.0 was able
to discover and execute those tests, but Project, Auth, and Submission all
reported every valid mutant as surviving (`K 0`). That cross-service pattern
was treated as a tooling-validation failure rather than a genuine 0% mutation
baseline.

Phase 1 therefore keeps the official xUnit v3 unit/mock suite unchanged and
adds small xUnit v2 mutation harnesses that reference the same production
projects. Stryker runs only through those harnesses.

## Projects

- `tests/ResearchTrack.ProjectService.MutationTests`
  - `ProjectMilestonePolicyTests`
  - `ProjectRequestValidatorTests`
- `tests/ResearchTrack.AuthService.MutationTests`
  - `PasswordPolicyValidatorTests`
  - `PasswordPolicyValidatorBoundaryTests`
- `tests/ResearchTrack.SubmissionService.MutationTests`
  - file rules
  - responsibility/authorization rules
  - workflow/review decision rules

These projects are deliberately not added to `ResearchTrack.sln`, so normal
unit/mock CI and Sachith's coverage evidence remain based only on the official
xUnit v3 projects. Mutation testing is isolated through its own scripts and
manual GitHub Actions workflow.


## Stryker project discovery

Each mutation harness has exactly one direct production `ProjectReference`.
Stryker is executed from that harness directory and therefore auto-detects the
production project under test. The config intentionally does not pass a path
through the `project` option; Stryker expects a project file name there, and no
option is needed when there is a single project reference.

## No database dependency

The mutation harnesses contain only deterministic business-rule tests. They do
not contain the `DatabaseIntegration` suite and require no
`RESEARCHTRACK_TEST_*_CONNECTION` environment variables.

## Proving the harness observes production changes

Run:

```bash
bash ./scripts/mutation-harness-verify.sh
```

The script performs three checks:

1. All three mutation harnesses pass against normal production code.
2. It temporarily changes the Project milestone due-date boundary from
   `< today` to `<= today` and expects the Project harness to fail.
3. It restores the source file and proves the Project harness passes again.

The source restoration is protected by a shell `trap`, so the original file is
restored even if the command is interrupted.

A successful verification proves that the dedicated mutation test assembly is
actually consuming the production assembly and can detect a deliberately
injected defect before Stryker is trusted.

## Running the baseline

After harness verification:

```bash
bash ./scripts/mutation-baseline.sh project
bash ./scripts/mutation-baseline.sh auth
bash ./scripts/mutation-baseline.sh submission
```

or:

```bash
bash ./scripts/mutation-baseline.sh all
```

Reports are written under `artifacts/mutation/`.

## Threshold policy

`break` remains `0` during the first valid baseline. A CI regression threshold
will be chosen only after real killed/survived results are measured and
meaningful survivors are reviewed.
