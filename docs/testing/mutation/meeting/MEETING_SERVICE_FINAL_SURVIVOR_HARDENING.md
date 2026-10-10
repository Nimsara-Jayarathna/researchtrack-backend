# MeetingService final survivor hardening

This pass hardens both MeetingService business-service mutation scopes while keeping them separate from GitHubService execution and reporting.

The channel-service hardening covers role normalization, supervisor/student authorization branching, exact editable-field length boundaries, HTTP/HTTPS link validation, cross-project update/delete protection, and approval state guards.

The record-service hardening covers role normalization, exact `yyyy-MM-dd` date parsing, positive-duration boundaries, summary/details normalization and maximum lengths, cross-project channel and record protection, and approval state guards.

Run the focused MeetingService pass with:

```bash
bash ./scripts/mutation-baseline.sh meeting
```

The two Meeting subscopes remain independent in the generated mutation summary: `channel-service` and `record-service`.
