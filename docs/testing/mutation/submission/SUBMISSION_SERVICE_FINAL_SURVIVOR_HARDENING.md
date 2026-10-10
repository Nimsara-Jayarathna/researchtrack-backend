# SubmissionService Final Survivor Hardening

## Baseline before this pass

- Weighted SubmissionService: **45.41%** (262 killed / 315 survived)
- Rules: **77.89%**
- Requirement service: **34.93%**
- Research submission service: **40.77%**

## Hardening focus

The final pass strengthens mutation-resistant assertions for requirement ordering and lifecycle transitions, exact due-date and file-size boundaries, deletion and update guards, active-upload protection, submission/revision eligibility, stale versus active upload sessions, storage authorization failures, metadata validation, owner and expiry checks, persistence snapshots, late-submission boundaries, review transitions, approval/rejection behavior, and download submission/version matching.

The normal xUnit v3 suite uses `TestContext.Current.CancellationToken`. The dedicated xUnit v2 Stryker harness uses `CancellationToken.None`.

`SubmissionRequirementService.ListAsync` deliberately keeps the project predicate in the database but performs the final presentation ordering after materialization. This preserves deterministic ordering while avoiding SQLite's inability to translate `DateTimeOffset` ordering during unit/mutation tests.

## Verification

```bash
bash ./scripts/mutation-baseline.sh submission
```

The command verifies the normal unit/mock suite, verifies the dedicated mutation harness, runs the rules, requirement-service, and submission-service Stryker scopes, and refreshes the mutation summary.

Do not average subscope percentages. The service score is `killed / (killed + survived)`. Ignored and compile-error mutants remain visible but are excluded from the surviving-business-mutant denominator.
