# SCRUM-179 sprint discovery and Jira UI consistency fixes

- Re-resolves Jira board metadata during each sync instead of trusting stale persisted board type.
- Automatically selects a deterministic Scrum board for older/no-board connections.
- Logs issue/board/sprint/membership counts for sync diagnostics.
- Keeps atomic local snapshot publication and stale-good data semantics.
- Frontend centers Jira secondary navigation and metric cards.
- Frontend reuses BlockingState for Jira loading states.
- Frontend reuses GitHub-style LastSyncedBadge and SyncStatusBadge through JiraSyncMeta.
