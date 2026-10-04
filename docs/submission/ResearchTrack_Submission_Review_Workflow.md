# ResearchTrack submission review and revision workflow

This implementation extends the secure S3 submission foundation without changing the storage transport. Completed S3 version objects remain immutable. Business state is stored in MySQL.

## State machine

```text
OPEN requirement + no submission
        -> Student uploads V1
        -> PENDING_REVIEW

PENDING_REVIEW
        -> APPROVED          (locked)
        -> REJECTED          (locked)
        -> CHANGES_REQUESTED (Student may upload exactly the next version while requirement stays OPEN)

CHANGES_REQUESTED + OPEN requirement
        -> Student uploads V2/V3/...
        -> PENDING_REVIEW
```

Formal reviews are immutable and version-specific. `CHANGES_REQUESTED` and `REJECTED` require feedback. The separate submission-comments feature has been removed; formal review feedback is the authoritative Supervisor message for a reviewed version.

## APIs

- `GET /api/v1/projects/{projectId}/submissions`
- `GET /api/v1/projects/{projectId}/submissions/{submissionId}`
- `POST /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/upload-sessions`
- `POST /api/v1/projects/{projectId}/submissions/upload-sessions/{uploadSessionId}/complete`
- `GET /api/v1/projects/{projectId}/submissions/{submissionId}/versions/{versionId}/download-url`
- `POST /api/v1/projects/{projectId}/submissions/{submissionId}/reviews` (Supervisor only + project manage check)

## Concurrency and integrity rules

- One logical submission per `(ProjectId, RequirementId)`.
- One formal review per `VersionId`.
- Review requests must target the exact `CurrentVersionId`; stale requests return `409`.
- Only `CHANGES_REQUESTED` permits a later Student version.
- The backend allocates `VersionCount + 1`; the browser never chooses a version number.
- Only one active upload session can exist for a requirement/version slot.
- Completion re-checks requirement status, submission status, current review, and expected version before recording the new version.
- Previous S3 objects, versions, and formal reviews are never overwritten by a revision.
- New or changed deadlines must be in the future. Existing historical deadlines may remain unchanged during unrelated requirement edits.
