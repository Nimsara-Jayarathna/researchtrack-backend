# MeetingService mutation hardening

[Documentation index](../../README.md) · [Runbook](../../runbook.md) · [Baseline and regression policy](../../baseline.md)

These records preserve successive hardening passes. Scores below are historical measurements that motivated each pass, not newly verified results. Use the shared runbook for current service commands and the baseline document for scoring and gate status.

## Channel and record hardening

This pass hardens both MeetingService business-service mutation scopes while keeping them separate from GitHubService execution and reporting.

The channel-service hardening covers role normalization, supervisor/student authorization branching, exact editable-field length boundaries, HTTP/HTTPS link validation, cross-project update/delete protection, and approval state guards.

The record-service hardening covers role normalization, exact `yyyy-MM-dd` date parsing, positive-duration boundaries, summary/details normalization and maximum lengths, cross-project channel and record protection, and approval state guards.

The two subscopes remain independent in the generated summary: `channel-service` and `record-service`.

## Verification

Run `bash ./scripts/mutation-baseline.sh meeting` from the repository root. This verifies both test suites and executes every finalized MeetingService subscope. Follow the [runbook](../../runbook.md) for reporting and survivor review.
