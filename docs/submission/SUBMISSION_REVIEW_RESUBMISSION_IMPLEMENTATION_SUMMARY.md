# ResearchTrack Submission Workflow UX + Review Update

## Backend

- Secure S3 upload/session/version flow retained unchanged.
- Immutable version-specific `SubmissionReview` remains the only formal Supervisor feedback mechanism.
- Removed the separate `SubmissionComment` domain, API, service methods, and runtime EF model.
- Added forward migration `20261004030000_RemoveSubmissionComments` so existing databases safely drop `submission_comments` without rewriting already-applied history.
- Added deadline validation: new deadlines must be future; changed deadlines must be future; an already-existing historical deadline may remain unchanged during other edits.
- Multi-submission MySQL query workaround remains intact.

## Supervisor frontend

- Replaced the split review/requirements presentation with one workflow-oriented Research Submissions workspace.
- Ordering now prioritizes: Needs review -> Waiting on revision -> Open requirements -> Completed reviews -> Closed/archived.
- Pending review cards receive the strongest primary action: `Review submission`.
- Approved/rejected items are intentionally lower in the page.
- Requirement-management actions are moved into a compact overflow menu so Edit/Close/Archive/Delete do not compete with review actions.
- Requirement status and submission status are visually separated.
- Removed redundant `Approved version recorded` copy.
- Submission detail focuses on current version, formal decision, and immutable version history; comment UI is removed.
- Requirement editor now uses separate date/time controls, a No deadline option, future-only selection, and clearer file-type/size controls.
- Existing submission history is explicitly protected when requirement constraints change.

## Student frontend

- Ordering now prioritizes changes requested and unsubmitted open requirements, followed by pending review, completed submissions, then closed/archived items.
- Formal Supervisor feedback remains prominent for `CHANGES_REQUESTED`.
- Existing direct-S3 upload/resubmission and immutable version history remain unchanged.
- Comment UI/API usage is removed.

## Migration

After `20261004003000_AddSubmissionReviewWorkflow` has been applied, run the normal SubmissionService migration command. The new migration removes only `submission_comments`.
