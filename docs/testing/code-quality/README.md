# ResearchTrack — C# Code Quality (SE3112)

**Scope:** only production C# files under `src/`; **does not modify** tests, test/mutation/performance tooling or existing GitHub Actions workflows. SonarQube and CI quality gates are separate later milestones.

## Design

- **CSharpier 1.3.0** is the sole formatter for production C# (110-character print width, four spaces, LF endings). The pinned version lives in `scripts/code-quality/.config/dotnet-tools.json`, **not** the existing `.config/dotnet-tools.json` used by EF, ReportGenerator and Stryker.
- **`src/.editorconfig`** contains only production-source IDE rules; inherits the existing root `.editorconfig`. IDE0055 is disabled *within `src`* to prevent CSharpier/dotnet-format whitespace conflicts. Both IDE0007/IDE0008 are disabled to avoid a pointless 1000+ declaration rewrite; existing `var` preferences are not changed.
- **`dotnet format style`** is used only for safe, explicitly selected IDE0005 (unused usings) and IDE0065 (using placement). It is *not* used for whitespace formatting.
- **Roslyn** performs production-project builds and, via opt-in `src/Directory.Build.targets`, emits native SARIF in `analyze`. CA150x, resource and logging findings are **review-only**, not auto-fixed. Existing warnings are recorded without treating all historical warnings as gate failures.
- Commands operate on eight discovered production `.csproj` files. No unit test, mutation, coverage or load test command is invoked.

## Install and run (Windows Git Bash, .NET SDK 10.0.401)

```bash
# from the repo root, on a separate feature branch:
git switch -c feature/SE3112-automated-code-quality

# 1. One-time tool restore from NuGet (network required):
bash ./scripts/code-quality.sh doctor
bash ./scripts/code-quality.sh setup

# 2. Apply automated IDE-import fixes and CSharpier to the production code:
bash ./scripts/code-quality.sh fix first-format

# 3. Ensure it is repeatable, and compile production projects:
bash ./scripts/code-quality.sh check after-format

# 4. Re-run CSharpier fix; the second pass should have no source diffs:
git diff --stat -- src/
bash ./scripts/code-quality.sh fix idempotence

# 5. Native analyzer report (review-only suggestions; compilation errors fail):
bash ./scripts/code-quality.sh analyze native-after-format

# 6. Review source changes before committing:
git diff --check
git diff --stat -- src/
git diff -- src/
```

**Shortcut:** `bash ./scripts/code-quality.sh bootstrap first-format` runs `fix` and then `check`. Use `setup` first if you want to diagnose NuGet restoration separately.

After `fix`, review the changes and commit the pure formatting separately. **Do not automatically stage or commit all changes.** If you already have a feature branch with uncommitted changes, preserve them first. A successful compile alone does not establish behavioral equivalence.

### Exit codes and artifacts

- `fix` returns nonzero for formatter/IDE-tool failure or protected-file drift; it may leave the eligible files it formatted before failing. Review changes rather than assuming transactionality.
- `check` is the *local quality gate*: CSharpier check, selected IDE style verification, and eight production builds. Any tool failure or actionable violation makes it fail. **It never writes source files.**
- `analyze` rebuilds the eight production projects with an opt-in Roslyn analysis configuration, writes real SARIF and does not gate on historical suggestion/warning findings. A compilation/tool failure remains a failure.
- Source files are individually passed to CSharpier in bounded batches to avoid accidental XML formatting and Windows argument-length issues. `--ignore-path` supplies a second exclusion layer.
- `artifacts/code-quality/<run-id>/code-quality-summary.md`, `manifest.json`, `status.tsv`, `raw/*.log`, and `roslyn/*.sarif` contain tool evidence and honest per-stage statuses. The entire artifacts directory is already in `.gitignore`.

### Special exclusions

Never automatically rewrite: `tests/`, `tools/`, `.github/`, existing mutation/k6 scripts or test configurations, `**/Migrations/**`, generated `.g.cs`, `.Designer.cs`, `.generated.cs`, `bin/`, `obj/`, or XML project files. These generated files remain in the existing .NET compilation graph as required by the application; they are excluded **from formatter modifications**, not removed from application builds.

`fix` hashes protected directories and files before and after. The command reports unintended changes; it cannot automatically undo arbitrary changes if a tool misbehaves.

## Meaning of the automated rules

| Kind | Owner | Auto-fix? | Blocks `check`? |
|---|---|---|---|
| Whitespace, wrapping, indentation, line endings | CSharpier | Yes | Yes |
| Unused using directives, using placement (IDE0005/IDE0065) | Roslyn `dotnet format style` | Yes | Yes |
| C# syntax/compilation errors | dotnet build | No | Yes |
| CA1501/2/5/6, CA2000, CA2208, CA1848, CA1873 | Roslyn `analyze` | No | No new quality gate yet |
| IDE0007/IDE0008 preferences | Existing .editorconfig; suppressed diagnostics under src | No | No |

**Do not run** `dotnet format ResearchTrack.sln` to format this code: it includes tests and may rewrite EF migrations. Use the explicit quality scripts.

## Validate the runner without changing code

```bash
bash ./scripts/code-quality.sh self-test
```

This exercises the shell script using a fake `dotnet`. It is **not** proof that the real formatter or production project builds pass. For a real end-to-end verification, run `setup`, `fix`, `check`, and `analyze` on your .NET 10 workstation.

To test the fail/fix/pass behavior, make a temporary, deliberately badly spaced edit in an *eligible production C# file on a disposable branch*, run `check` (expect nonzero), `fix` (expect formatting to change it), and `check` again (expect zero). Finally revert the intentionally bad edit and review the diff. Do not use migrations or test-source files for this.

## Known execution limitation of the supplied ZIP

The code-quality toolchain and the complete original backend are packaged together. In the packaging environment, **the .NET SDK and NuGet network access are unavailable**, so this ZIP cannot honestly claim to contain a formatter-produced whole-backend rewrite. The first genuine `fix` on your workstation performs that formatting, using the pinned official CSharpier version. Keep the original files and the `first-format` diff as before/after evidence. Do not replace this with regex or hand-rolled formatting.

## SonarQube Cloud CI-based .NET analysis (separate workflow)

The dedicated cloud analysis is implemented in
[SONARQUBE_CLOUD.md](SONARQUBE_CLOUD.md) and
`.github/workflows/backend-sonarqube.yml`. Use
`bash ./scripts/code-quality.sh sonar [run-id]` for a manual main-branch scan
once the token/project/organization environment variables are set. The native
`fix`, `check`, and `analyze` commands remain independent and unchanged.
