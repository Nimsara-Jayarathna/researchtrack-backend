# Mutation-testing implementation history

[Documentation index](../README.md) · [Current runbook](../runbook.md) · [Baseline and regression policy](../baseline.md)

This consolidated record replaces the overlapping phase guides, implementation summaries, xUnit compatibility fix, and tool-manifest fix. It describes superseded setup decisions; use the runbook for current commands.

## Initial baseline: phases 1–4

The initial `feature/SE3112-mutation-testing` work followed the unit/mock suite and its 65% line / 50% branch business-coverage gate. It pinned Stryker.NET 5.0.0, selected deterministic rules, added the baseline runner and Python summary generator, and introduced a separate manual mutation workflow.

The original targets were:

- Project: `ProjectMilestonePolicy` and `ProjectRequestValidator`.
- Auth: `PasswordPolicyValidator`.
- Submission: `SubmissionFileRules`, `SubmissionResponsibilityRules`, and review workflow rules in `SubmissionConstants`.

The first design reused the normal test projects directly. Configurations used the `Standard` mutation level, HTML/JSON/Markdown reporting, and `break: 0` while an untouched baseline was measured. Survivor review was intended to strengthen normal unit tests for meaningful behavior, document equivalent mutants, and narrow scope only for justified non-business noise. The initial packaging environment lacked the .NET SDK, so its configuration checks did not establish a real mutation baseline.

## xUnit v3 compatibility investigation

The initial Project run discovered the normal tests but reported 0 killed / 196 survived after coverage capture failed. This result was rejected as evidence. The first attempted correction added `Category!=DatabaseIntegration` and disabled coverage analysis while retaining VSTest; the MTP path did not expose the filtering option needed by that setup. The summary runner also gained `python3` fallback support and generated Python caches were ignored.

Project, Auth, and Submission continued to show a cross-service zero-killed pattern. The repository therefore adopted dedicated xUnit v2 mutation harnesses instead of accepting those xUnit v3 results as a 0% baseline.

## Dedicated harness correction

The initial harnesses covered Project milestone/request rules, Auth password-policy boundaries, and Submission file/responsibility/workflow rules. They reference the same production projects and stay outside `ResearchTrack.sln`, preserving the normal xUnit v3 CI and coverage evidence. Internal rule access uses `InternalsVisibleTo` where necessary.

Each harness has one direct production project reference, allowing Stryker to discover its target from the harness directory without a `project` path override. Deterministic harness tests avoid external database integration and associated connection environment variables.

The controlled verification script checks that the three original harnesses pass on normal code, that the Project harness fails after a temporary `< today` to `<= today` boundary defect, and that it passes after source restoration. The separate mutation runner and manual workflow evolved to use the dedicated harnesses. Initial verification and threshold-zero instructions belong to this historical phase; current regression settings are described in the baseline document.

## Canonical tool manifest

Two competing root-marked manifests previously existed: `.config/dotnet-tools.json` for Stryker/reporting and a root `dotnet-tools.json` for EF. Nested harness commands could resolve the wrong manifest and report `dotnet-stryker does not exist`.

The canonical [.config/dotnet-tools.json](../../../../.config/dotnet-tools.json) now contains EF 10.0.9, ReportGenerator 5.5.11, and Stryker.NET 5.0.0. The runner explicitly restores that manifest and invokes Stryker through `dotnet tool run dotnet-stryker --`.

## Portfolio expansion

Mutation testing expanded from Project/Auth/Submission rules to selected business logic across Auth, Project, Submission, GitHub, Jira, Gateway, and Meeting. The [strategy](../strategy.md) defines those boundaries. Service records preserve survivor hardening and scope refinements; the [baseline document](../baseline.md) separates recorded scores from current gate configuration.
