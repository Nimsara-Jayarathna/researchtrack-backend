# SubmissionService final survivor-hardening pass

Baseline before this pass:

- Rules: 77.89% (74 killed / 21 survived)
- Requirement service: 60.27% (88 killed / 58 survived)
- ResearchSubmissionService: 52.68% (177 killed / 159 survived)
- Weighted SubmissionService: 58.75% (339 killed / 238 survived)

The rules and requirement scopes are frozen. This pass targets only the remaining high-value `ResearchSubmissionService` orchestration survivors.

Added assertions cover revision eligibility and version increments, stale-session replacement when responsibility changes, revision race/invariant guards, successful version-two persistence, approval reset on resubmission, exact due-date late semantics, final-metadata fallback, reviewer/approval snapshots, stale/missing/duplicate review paths, and exact download-grant forwarding.

Run only the final weak scope with:

```bash
bash ./scripts/mutation-baseline.sh submission-final
```

The normal Submission unit/mock suite and dedicated mutation harness are verified first. Do not change production behavior merely to improve the score; remaining survivors should be classified after this pass.
