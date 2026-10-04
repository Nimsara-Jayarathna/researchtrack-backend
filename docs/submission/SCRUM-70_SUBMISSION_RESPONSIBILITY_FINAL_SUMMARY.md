# SCRUM-70 — Submission Responsibility Final Implementation

## Final business rule

Each submission requirement has exactly one official submitter authority.

- `PROJECT_LEADER` is the default responsibility mode.
- The Supervisor may instead select one active project Student using `ASSIGNED_STUDENT`.
- Only the resolved responsible Student may create or finalize an official V1/V2/V3 submission version.
- Other project Students can still see the requirement, feedback, submitted versions, preview supported files, and download files, but cannot submit an official version.
- Authorization is enforced in the SubmissionService as well as reflected in the UI.

## No Project Leader flow

When `PROJECT_LEADER` is selected but the project has no leader:

- the requirement editor shows: `No Project Leader has been assigned to this project. Assign a leader first, or choose a specific student.`
- Create/Save remains disabled while the invalid leader option is selected;
- the Supervisor can choose a specific active Student instead; or
- `Go to member management →` routes directly to the Project Details Team/member-management tab so a Project Leader can be assigned.

The Supervisor requirement cards also surface a `Manage project team` action when an existing requirement currently has no valid responsible submitter.

## Leader changes and historical truth

`PROJECT_LEADER` requirements resolve the current leader dynamically for future uploads. Therefore, if leadership changes, the new leader becomes responsible for the next permitted version.

Every completed `SubmissionVersion` stores a submitter snapshot:

- actual submitter ID/name;
- `SubmitterRoleSnapshot` (`PROJECT_LEADER` or `ASSIGNED_STUDENT`);
- `ResponsibilityModeSnapshot` used by the requirement when that version was submitted.

This means a historical V1 submitted by Alice while she was Project Leader continues to display `Submitted by Alice · Project Leader` even after Bob becomes the current Project Leader. Existing history is never relabelled from current membership state.

If a Supervisor explicitly assigns the current leader as the specific Student, the version still snapshots that person as `PROJECT_LEADER`, while `ResponsibilityModeSnapshot` records that the requirement was using `ASSIGNED_STUDENT` delegation.

## Assigned Student lifecycle

For `ASSIGNED_STUDENT` requirements:

- the selected Student must be an active member of that project when the requirement is created/updated;
- if that Student later leaves the project, uploads are blocked rather than silently reassigned;
- the UI shows that reassignment is required and provides access to Team/member management;
- the Supervisor must explicitly choose a replacement Student or switch the requirement to Project Leader.

Changing responsibility only controls future uploads. Existing versions retain their original submitter snapshot.

## Upload / resubmission enforcement

The same responsibility rule applies to initial submission and every revision.

The backend checks authority when the upload is started and checks it again when the upload is finalized. If leadership or assignment changes during an in-progress upload, the old user cannot finalize the official version.

A stale active upload belonging to the previous responsible Student is released when the newly responsible Student starts the same expected version, so a leadership/assignment change does not leave the requirement blocked until the old session timeout.

## API / contract changes

Submission requirement contracts now carry responsibility information including:

- responsibility mode;
- assigned Student snapshot fields;
- currently resolved responsible Student;
- responsible Student role;
- whether reassignment is required.

Submission version responses include the submitter role and responsibility mode snapshots used by version-history UI.

The SubmissionService obtains the current Project Leader and active Student membership from ProjectService; it does not become the source of truth for project membership.

## Frontend behavior

### Supervisor

- Create/Edit Requirement defaults to Project Leader.
- Can delegate to one active Student.
- Missing leader shows the warning and a direct member-management route.
- Removed assignee requires explicit reassignment.
- Requirement cards show the currently responsible Student.
- Submitted cards/version history show the actual historical submitter plus the submitter role snapshot.

### Student

- The existing accordion UI remains.
- `Submit document` / `Upload revised version` only appears for the currently responsible Student.
- Another member sees who is assigned instead of receiving an upload control.
- Missing responsibility shows a waiting-for-assignment state.
- Version history shows `Submitted by <name> · Project Leader` or `· Assigned submitter` from the historical version snapshot.
- Existing PDF/DOCX preview behavior and disabled PPTX/ZIP preview behavior are preserved.

## Database migration

New migration:

`20261004093000_AddSubmissionResponsibility`

It adds responsibility fields to `submission_requirements` and submitter/responsibility snapshot fields to `submission_versions`.

Existing requirements default to `PROJECT_LEADER`. Existing historical versions keep null snapshot fields because the system cannot safely infer what role the uploader held before this feature existed.

Apply using the repository's normal SubmissionService migration flow, for example:

```bash
./scripts/migrate.sh submission
```

## Validation performed in this environment

- 466 frontend TS/TSX files parsed with 0 syntax diagnostics.
- 466 frontend source files checked for relative import resolution with 0 missing relative imports.
- 52 SubmissionService/test C# files passed structural delimiter checks.
- Explicit FK/index/PK identifiers checked for MySQL's 64-character identifier limit.
- Responsibility migration Designer and DbContext snapshot both contain the expected responsibility model fields/index.
- Confirmed no accidental responsibility-snapshot property exists on `SubmissionUploadSession`.
- User-facing submission UI scan found no S3/presigned-URL/object-storage/immutability explanatory copy; internal implementation identifiers remain internal.

The current environment does not have the .NET SDK installed, so an actual `dotnet build/test` could not be run here. Full `npm ci` is also not possible offline because the existing `zxcvbn` package is not cached. Run the repository's normal backend and frontend CI commands locally/CI before merge.
