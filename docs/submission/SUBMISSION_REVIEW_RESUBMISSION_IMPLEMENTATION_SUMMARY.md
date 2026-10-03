# ResearchTrack Submission Review + Resubmission Implementation

## Scope implemented

The supplied SCRUM-68 backend/frontend were extended without replacing the existing private-S3 upload engine.

### Backend

- Immutable formal `SubmissionReview` records tied to the exact `SubmissionVersion`.
- Append-only `SubmissionComment` records, optionally tied to a version.
- Formal decisions: `APPROVED`, `CHANGES_REQUESTED`, `REJECTED`.
- Feedback required for changes-requested and rejected decisions.
- Owning-Supervisor authorization for formal review.
- Stale review protection: request VersionId must equal CurrentVersionId.
- Exact ApprovedVersionId/ApprovedAt recording.
- Existing upload-session flow generalized from V1-only to next-version resubmission.
- Resubmission allowed only for `CHANGES_REQUESTED + OPEN`.
- Backend assigns `VersionCount + 1`; browser cannot choose a version number.
- Completion re-checks state before committing a revision.
- Previous versions and reviews remain immutable.
- Comments are status-neutral and have no edit/delete API.
- New EF migration: `20261004003000_AddSubmissionReviewWorkflow`.
- MySQL FK/index identifiers use explicit names below MySQL's 64-character limit.

### Supervisor frontend

- Pending-review queue and status summary.
- Submission detail view with all versions.
- Preview/download any version using the existing short-lived S3 GET grant.
- Approve / Request changes / Reject workflow.
- Required feedback handling.
- Stale `409` refresh behavior.
- Full historical formal-review display.
- Shared append-only comments.
- Existing requirement CRUD/lifecycle retained.

### Student frontend

- Formal feedback displayed on the current reviewed version.
- `Upload revised version` only for `CHANGES_REQUESTED + OPEN`.
- Same secure direct-S3 uploader reused for V2/V3/etc.
- Full version history with current/approved/review markers.
- Preview/download all authorized versions.
- Shared append-only comments.
- Upload remains locked for pending-review, approved, rejected, closed, and archived states.

## State machine

```text
OPEN requirement
      |
      | Student uploads V1
      v
PENDING_REVIEW
      |
      +--> APPROVED ---------> locked
      |
      +--> REJECTED ---------> locked
      |
      +--> CHANGES_REQUESTED
                |
                | Student uploads next immutable version
                v
          PENDING_REVIEW
```

Each formal decision is attached to the exact version that was reviewed.

## Storage behavior

No new storage architecture was introduced. Each initial/revised version uses the existing flow:

```text
create upload session
       -> browser PUT to private S3 pending key
       -> backend HEAD/verify
       -> server-side promote/copy to immutable final version key
       -> DB version commit
```

Completed versions have no delete/replace endpoint.

## Migration

If `AddSubmissionFoundation` is already correctly applied, run the normal SubmissionService migration flow. The new workflow migration adds only:

- `submission_reviews`
- `submission_comments`

No new S3 environment variables are required for this workflow.

## Verification performed in the artifact environment

- Parsed all frontend TS/TSX files with TypeScript parser: no syntax failures.
- Checked all internal frontend imports resolve to repository files.
- Checked C# source files for gross delimiter-balance errors: none found.
- Checked generated migration identifiers: no identifier exceeds MySQL's 64-character limit.
- Confirmed migration Designer target model and ModelSnapshot target model are structurally aligned.

Full CI could not be executed in this environment: the .NET SDK is unavailable, and `npm ci --offline` cannot restore uncached `zxcvbn`. Run the repository's normal backend tests/build and `npm run ci` locally/CI before merge.
