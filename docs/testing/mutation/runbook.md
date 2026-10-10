# Mutation-testing runbook

[Documentation index](README.md) · [Strategy](strategy.md) · [Baseline and regression policy](baseline.md)

## Prerequisites

Use a .NET 10 SDK compatible with [global.json](../../../global.json), Bash (Git Bash or WSL on Windows), and Python 3 for summary generation and weighted gate evaluation. The dedicated harnesses require no external test database.

The runner restores the pinned tools from [.config/dotnet-tools.json](../../../.config/dotnet-tools.json), including Stryker.NET 5.0.0. Manual restoration is available with:

```bash
dotnet tool restore --tool-manifest .config/dotnet-tools.json
```

## Run a service or the full portfolio

From the repository root:

```bash
bash ./scripts/mutation-baseline.sh auth
bash ./scripts/mutation-baseline.sh project
bash ./scripts/mutation-baseline.sh submission
bash ./scripts/mutation-baseline.sh github
bash ./scripts/mutation-baseline.sh jira
bash ./scripts/mutation-baseline.sh gateway
bash ./scripts/mutation-baseline.sh meeting
```

Each service command runs every configured subscope for that service. The interface has no registration-only, webhook-only, or submission-orchestration-only target.

For final evidence, run:

```bash
bash ./scripts/mutation-baseline.sh all
```

For each selected service, [the runner](../../../scripts/mutation-baseline.sh) verifies the normal unit/mock suite with `Category!=DatabaseIntegration`, verifies the dedicated mutation harness, and then executes Stryker with the service's `configs/*.json` files. Each selected subscope's previous output directory is replaced before its run.

## Reports and summary

Stryker writes HTML, JSON, and Markdown reports beneath:

```text
artifacts/mutation/
  <service>/
    <subscope>/
  mutation-summary.md
```

For Auth, the subscopes are `password-policy`, `authentication`, `password-reset`, `registration`, `user-account`, and `user-directory`. Other service subscopes are listed in the [strategy](strategy.md).

The runner selects `python3`, `python`, or Windows `py -3` after verifying Python 3. If none is available, it warns and skips summary generation, including weighted gate evaluation. Python 3 is therefore required for complete final evidence.

To regenerate the summary from existing reports:

```bash
python scripts/mutation-summary.py artifacts/mutation
```

Only `all` adds `--enforce` automatically. Single-service summaries may include older reports for other services; use the complete run for final project-wide evidence. See [baseline and regression policy](baseline.md) for the missing-policy-file limitation in this checkout.

## Review survivors

Open the affected subscope's HTML report. Classify survivors as meaningful test weaknesses, equivalent mutants, or justified non-business transformations. Strengthen the normal xUnit v3 tests for meaningful behavior, then mirror/adapt the scenario into the xUnit v2 harness and rerun the service command. Avoid broad exclusions or diagnostic-string assertions solely to raise scores.

## Controlled harness verification

For the original Project/Auth/Submission harness sanity check:

```bash
bash ./scripts/mutation-harness-verify.sh
```

The script runs those three harnesses, temporarily changes the Project milestone due-date boundary from `< today` to `<= today`, expects the Project harness to fail, restores the source through a shell trap, and verifies that the Project harness passes again. Run it with no concurrent edits to that production file. This check is separate from the normal service runner and does not verify every service through an injected defect.

## CI and troubleshooting

The manual [backend mutation workflow](../../../.github/workflows/backend-mutation.yml) exposes the same service choices, builds the solution, verifies the normal unit/mock suite, runs the selected mutation scope, and uploads available evidence for 30 days.

- **Stryker tool cannot be resolved:** restore the canonical `.config/dotnet-tools.json` manifest and use the runner, which invokes `dotnet tool run dotnet-stryker --` from each harness directory.
- **Unexpected zero-killed results:** verify the dedicated harness observes production changes before accepting the score. The initial xUnit v3 results were rejected; see [implementation history](history/implementation.md).
- **Weighted gates show `N/A` or fail with no floor:** the default `config/testing/mutation-policy.json` file is absent in this checkout. See [baseline and regression policy](baseline.md).
