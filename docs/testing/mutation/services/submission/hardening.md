# SubmissionService mutation hardening

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

These records preserve successive hardening passes. Scores below are historical measurements that motivated each pass, not newly verified results. Use the shared runbook for current service commands and the baseline document for scoring and gate status.

## Requirement and workflow hardening

### Baseline before this pass

- Weighted SubmissionService: **45.41%** (262 killed / 315 survived)
- Rules: **77.89%**
- Requirement service: **34.93%**
- Research submission service: **40.77%**

### Hardening focus

The final pass strengthens mutation-resistant assertions for requirement ordering and lifecycle transitions, exact due-date and file-size boundaries, deletion and update guards, active-upload protection, submission/revision eligibility, stale versus active upload sessions, storage authorization failures, metadata validation, owner and expiry checks, persistence snapshots, late-submission boundaries, review transitions, approval/rejection behavior, and download submission/version matching.

The normal xUnit v3 suite uses `TestContext.Current.CancellationToken`. The dedicated xUnit v2 Stryker harness uses `CancellationToken.None`.

`SubmissionRequirementService.ListAsync` deliberately keeps the project predicate in the database but performs the final presentation ordering after materialization. This preserves deterministic ordering while avoiding SQLite's inability to translate `DateTimeOffset` ordering during unit/mutation tests.

## Submission orchestration follow-up

Baseline before this pass:

- Rules: 77.89% (74 killed / 21 survived)
- Requirement service: 60.27% (88 killed / 58 survived)
- ResearchSubmissionService: 52.68% (177 killed / 159 survived)
- Weighted SubmissionService: 58.75% (339 killed / 238 survived)

The rules and requirement scopes are frozen. This pass targets only the remaining high-value `ResearchSubmissionService` orchestration survivors.

Added assertions cover revision eligibility and version increments, stale-session replacement when responsibility changes, revision race/invariant guards, successful version-two persistence, approval reset on resubmission, exact due-date late semantics, final-metadata fallback, reviewer/approval snapshots, stale/missing/duplicate review paths, and exact download-grant forwarding.

## Verification

Run `bash ./scripts/mutation-baseline.sh submission` from the repository root. This verifies both test suites and executes every finalized SubmissionService subscope. Follow the [runbook](../../runbook.md) for reporting and survivor review.
