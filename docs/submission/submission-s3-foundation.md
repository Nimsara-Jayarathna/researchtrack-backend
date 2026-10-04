# Submission S3 Foundation

This implementation completes the file-submission foundation used by the Research Submission stories. It intentionally stops before formal Supervisor review and Student resubmission.

## Implemented lifecycle

1. A Supervisor creates an `OPEN` submission requirement.
2. An authorized Student requests an upload session for that requirement.
3. SubmissionService validates project access, requirement state, extension, declared size, and MIME type.
4. SubmissionService creates a short-lived pre-signed S3 `PUT` for a `pending/{uploadSessionId}` object.
5. The browser uploads bytes directly to the private S3 bucket.
6. The Student completes the upload session.
7. SubmissionService performs `HEAD` verification against S3, including actual size and content type.
8. SubmissionService copies the verified pending object to the immutable final version key.
9. The database transaction creates the logical `ResearchSubmission` and immutable `SubmissionVersion` V1 with `PENDING_REVIEW` status.
10. The pending object is deleted best-effort. Completed versions have no delete API.

The final object key is never supplied by the browser. A still-valid pre-signed PUT can only modify the temporary `pending/` object and cannot overwrite a completed submission version.

## Extension points for later stories

The persistence model already separates `ResearchSubmission` from immutable `SubmissionVersion` records and includes the later lifecycle statuses `CHANGES_REQUESTED`, `APPROVED`, and `REJECTED`. No API in this implementation can transition to those states. A later resubmission story should reuse the same upload-session flow and allocate the next version number server-side.

## Required SubmissionService environment

Use `config/env/submission/.env.example` as the canonical contract. For normal AWS S3, leave `Storage__Endpoint` empty and configure:

```dotenv
Storage__Bucket=researchtrack-submissions
Storage__AccessKey=...
Storage__SecretKey=...
Storage__Region=ap-south-1
Storage__MaximumFileSizeBytes=10485760
Storage__PresignedUrlExpirySeconds=300
Storage__ForcePathStyle=false
```

For MinIO or another S3-compatible development endpoint, set `Storage__Endpoint` and usually `Storage__ForcePathStyle=true`.

## Private bucket and IAM

Keep Block Public Access enabled. The SubmissionService credential needs object permissions only for the configured bucket/prefixes. It must be able to put/copy/read metadata/delete temporary objects and generate signatures with its own credential. The frontend receives no AWS credentials.

Completed objects live below:

```text
projects/{projectId}/requirements/{requirementId}/submissions/{submissionId}/versions/{versionId}
```

Temporary objects live below:

```text
pending/{uploadSessionId}
```

An S3 lifecycle rule that expires old `pending/` objects is recommended as a second cleanup layer in addition to the application cleanup worker.

## S3 CORS

Direct browser `PUT` requires bucket CORS for the exact frontend origins. Example (replace origins with real Test/Production origins):

```json
[
  {
    "AllowedOrigins": ["https://researchtrack.example.com"],
    "AllowedMethods": ["PUT"],
    "AllowedHeaders": ["Content-Type"],
    "ExposeHeaders": ["ETag"],
    "MaxAgeSeconds": 300
  }
]
```

Do not use `*` for Production origins.

## API surface

Requirement management:

```text
GET    /api/v1/projects/{projectId}/submissions/requirements
POST   /api/v1/projects/{projectId}/submissions/requirements
PATCH  /api/v1/projects/{projectId}/submissions/requirements/{requirementId}
POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/close
POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/reopen
POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/archive
DELETE /api/v1/projects/{projectId}/submissions/requirements/{requirementId}
```

Initial Student submission:

```text
POST /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/upload-sessions
POST /api/v1/projects/{projectId}/submissions/upload-sessions/{uploadSessionId}/complete
GET  /api/v1/projects/{projectId}/submissions
GET  /api/v1/projects/{projectId}/submissions/{submissionId}
GET  /api/v1/projects/{projectId}/submissions/{submissionId}/versions/{versionId}/download-url
```

## Security invariants

- S3 bucket remains private.
- Pre-signed URLs are short-lived and never persisted as business data.
- Full pre-signed URLs are never logged by SubmissionService.
- The backend verifies the real S3 object before a submission version exists.
- Completion is idempotent.
- One logical submission exists per Project + Requirement.
- Only the Student who created an upload session can finalize that session.
- ProjectService authorization is checked for every read/write operation.
- Completed versions have no delete endpoint.
- Supervisor review and additional versions are intentionally not implemented in this slice.

## Local migration recovery after the pre-fix identifier failure

The original draft of `AddSubmissionFoundation` allowed EF to generate the foreign-key name
`FK_submission_upload_sessions_submission_requirements_RequirementId`, which is longer than
MySQL's 64-character identifier limit. The migration now uses explicit short constraint names,
and the EF model snapshot/designer are aligned with those names.

The migration also avoids a full unique index on the `varchar(1024)` S3 object-key column. With
`utf8mb4`, such an index can exceed MySQL/InnoDB's index-byte limit. Final object keys are generated
server-side from immutable GUID-based version paths, so this wide uniqueness index is unnecessary.

If a local database already attempted the broken migration and failed before EF recorded it as
applied, MySQL may have left one or more feature tables behind because DDL is not guaranteed to roll
back as one transaction. Confirm that `20261003210000_AddSubmissionFoundation` is absent from
`__EFMigrationsHistory`, then remove only the partially-created submission feature tables before
running the migration again:

```sql
SELECT *
FROM __EFMigrationsHistory
WHERE MigrationId = '20261003210000_AddSubmissionFoundation';

DROP TABLE IF EXISTS submission_upload_sessions;
DROP TABLE IF EXISTS submission_versions;
DROP TABLE IF EXISTS research_submissions;
DROP TABLE IF EXISTS submission_requirements;
```

Do not run the `DROP TABLE` statements against an environment where this migration was already
successfully applied or where submission data must be preserved.
