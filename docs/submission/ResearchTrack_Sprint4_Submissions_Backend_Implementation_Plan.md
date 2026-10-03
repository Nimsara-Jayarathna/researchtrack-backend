**# ResearchTrack Sprint 4 — Research Submission Management**

**## Backend Implementation Plan — FINAL IMPLEMENTATION CONTRACT**



**\*\*Status:\*\*** FINAL / implementation source of truth &#x20;

**\*\*Scope:\*\*** Stories 24–27 — Submission half of Sprint 4 &#x20;

**\*\*Target service:\*\*** \`ResearchTrack.SubmissionService\` &#x20;

**\*\*Primary persistence:\*\*** existing MySQL/EF Core persistence used by the current SubmissionService &#x20;

**\*\*File storage:\*\*** Azure Blob Storage &#x20;

**\*\*Architecture rule:\*\*** business state lives in MySQL; Azure Blob Storage stores file bytes only.



\---



**# 1. Why this document exists**



This document freezes the backend design for the Sprint 4 Research Submission Management implementation. When implementation starts, the code should follow this plan instead of redesigning the feature from scratch.



The implementation is based on the useful core behavior from the original SuperviseSuite Project Files feature:



\- direct browser-to-object-storage upload,

\- backend-generated temporary upload/download authorization,

\- file metadata stored separately from the binary,

\- server-side project authorization,

\- controlled file-type and size validation.



ResearchTrack extends that basic file repository into an actual academic submission workflow with:



\- Supervisor-managed submission requirements,

\- Student submission and resubmission,

\- immutable submission versions,

\- Supervisor approval / changes requested / rejection decisions,

\- formal feedback,

\- Student/Supervisor comments,

\- version-level review history,

\- Azure Blob Storage instead of S3,

\- Azure Identity based storage access through SubmissionService,

\- short-lived SAS URLs for direct upload and download.



This is **\*\*not\*\*** a generic project-files feature. It is a **\*\*Research Submission domain\*\***.



\---



**# 2. Current ResearchTrack baseline that must be preserved**



The current Sprint 4 backend already contains a dedicated service skeleton:



\`\`\`text

src/Services/ResearchTrack.SubmissionService/

&#x20;   Configuration/

&#x20;   Contracts/

&#x20;   Domain/

&#x20;   Extensions/

&#x20;   Features/

&#x20;   Infrastructure/

&#x20;   Persistence/

&#x20;   Program.cs

\`\`\`



Current important facts:



1\. \`SubmissionDbContext\` already exists.

2\. Persistence currently uses \`MySql.EntityFrameworkCore\` / \`UseMySQL(...)\`.

3\. JWT authentication is already wired through the shared ResearchTrack BuildingBlocks.

4\. The gateway already reserves canonical routes:



\`\`\`text

/api/v1/projects/{projectId}/submissions/{\*\*catch-all}

/api/v1/submissions/{\*\*catch-all}

\`\`\`



5\. ResearchTrack already has ProjectService authorization endpoints used by other microservices. SubmissionService must follow the same pattern rather than reading ProjectService tables directly.

6\. Existing GitHub/Jira/Meeting architecture conventions must remain intact.



\---



**# 3. Final stories covered by this backend**



**## Story 24 — Supervisor Submission Requirement Management**



Supervisor defines what the research group must submit.



**## Story 25 — Student Research Document Submission**



Student submits the first version of a required document through Azure Blob Storage.



**## Story 26 — Supervisor Submission Review & Feedback**



Supervisor reviews the current version and chooses APPROVED, CHANGES_REQUESTED or REJECTED, with feedback/comments.



**## Story 27 — Student Resubmission & Version History**



Student resubmits after CHANGES_REQUESTED and both roles can view immutable version/review history.



\---



**# 4. Non-negotiable architectural decisions**



These decisions are frozen unless a real implementation blocker is discovered.



**## 4.1 SubmissionService owns submission business state**



SubmissionService owns:



\- requirements,

\- logical submissions,

\- submission versions,

\- upload sessions,

\- formal reviews,

\- comments,

\- submission state transitions.



It does **\*\*not\*\*** own Research Project membership.



**## 4.2 ProjectService remains the authorization source**



SubmissionService must call the existing ProjectService authorization boundary.



Conceptual checks:



\`\`\`text

GET /api/v1/projects/{projectId}/authorization/access

GET /api/v1/projects/{projectId}/authorization/manage

\`\`\`



Use the same internal HTTP-client approach already established in ResearchTrack services such as Jira/GitHub.



Do not duplicate:



\`\`\`text

Project

ProjectMember

SupervisorProject

StudentProject

\`\`\`



inside SubmissionService.



**## 4.3 Blob Storage stores bytes; MySQL stores truth**



Azure Blob Storage is not the submission database.



Azure stores:



\`\`\`text

binary file bytes

blob metadata/content type

ETag / storage properties

\`\`\`



MySQL stores:



\`\`\`text

requirement

submission

version number

original file name

blob identity

submitter

review decision

feedback

comments

status

approved version

late flag

history metadata

\`\`\`



An expiring SAS URL must never be stored as permanent business data.



**## 4.4 Versions are immutable**



A resubmission creates a new \`SubmissionVersion\`.



Never overwrite V1 with V2.



Never change the blob backing an already completed version.



**## 4.5 One logical project submission per requirement**



The research group has one logical submission per requirement.



Recommended database constraint:



\`\`\`text

UNIQUE(ProjectId, RequirementId)

\`\`\`



Multiple students can upload different versions, but those versions belong to the same project submission.



**## 4.6 Formal review is version-specific**



APPROVED / CHANGES_REQUESTED / REJECTED applies to a specific \`SubmissionVersion\`.



This is mandatory because the system must prove which physical file was reviewed and approved.



\---



**# 5. Final domain model**



\`\`\`text

SubmissionRequirement

&#x20;       │

&#x20;       │ 1

&#x20;       ▼

ResearchSubmission

&#x20;       │

&#x20;       ├────────────── 1..N ─────────────► SubmissionVersion

&#x20;       │                                      │

&#x20;       │                                      └────► SubmissionReview

&#x20;       │

&#x20;       └──────────────────────────────────────► SubmissionComment



SubmissionUploadSession

&#x20;       │

&#x20;       └── temporary Azure upload authorization / completion handshake

\`\`\`



\---



**# 6. Enumerations**



**## 6.1 SubmissionRequirementStatus**



\`\`\`text

OPEN

CLOSED

ARCHIVED

\`\`\`



Meaning:



\- \`OPEN\`: initial submission/resubmission is allowed if the submission state also permits it.

\- \`CLOSED\`: requirement remains visible, but uploads are not allowed.

\- \`ARCHIVED\`: historical/read-only requirement; no new uploads or management except read.



**## 6.2 ResearchSubmissionStatus**



\`\`\`text

PENDING_REVIEW

CHANGES_REQUESTED

APPROVED

REJECTED

\`\`\`



Do not persist \`NOT_SUBMITTED\`.



\`NOT_SUBMITTED\` is derived when a requirement exists but no \`ResearchSubmission\` exists.



**## 6.3 SubmissionReviewDecision**



\`\`\`text

APPROVED

CHANGES_REQUESTED

REJECTED

\`\`\`



**## 6.4 SubmissionUploadSessionStatus**



\`\`\`text

PENDING

COMPLETED

EXPIRED

FAILED

\`\`\`



\---



**# 7. Entity design**



**## 7.1 SubmissionRequirement**



Recommended fields:



\`\`\`text

Id                  Guid PK

ProjectId           Guid, required

Title               varchar(200), required

Description         varchar/text, optional

DueAt               DateTimeOffset?, optional

AllowedFileTypes    persisted normalized set/string/JSON

MaxFileSizeBytes    long, required

Status              enum/string, required

CreatedBy           Guid, required

CreatedByName       varchar(200), required

CreatedAt           DateTimeOffset, required

UpdatedAt           DateTimeOffset?, optional

\`\`\`



Recommended indexes:



\`\`\`text

(ProjectId, Status)

(ProjectId, DueAt)

\`\`\`



Rules:



\- title required,

\- allowed types must be a subset of the service-wide supported allowlist,

\- maximum size must be positive and not exceed configured platform maximum,

\- due date is optional,

\- hard delete only when no submission/history exists,

\- when history exists use ARCHIVED instead of deleting.



**## 7.2 ResearchSubmission**



Recommended fields:



\`\`\`text

Id                  Guid PK

ProjectId           Guid, required

RequirementId       Guid, required

Status              enum/string, required

CurrentVersionId    Guid?, nullable until first completion transaction commits

VersionCount        int, required

LastSubmittedAt     DateTimeOffset, required

ApprovedVersionId   Guid?, nullable

ApprovedAt          DateTimeOffset?, nullable

CreatedAt           DateTimeOffset, required

UpdatedAt           DateTimeOffset?, nullable

\`\`\`



Constraints:



\`\`\`text

UNIQUE(ProjectId, RequirementId)

\`\`\`



Indexes:



\`\`\`text

(ProjectId, Status)

(RequirementId)

\`\`\`



**## 7.3 SubmissionVersion**



Recommended fields:



\`\`\`text

Id                  Guid PK

SubmissionId        Guid, required

VersionNumber       int, required

BlobName            varchar(1024), required

OriginalFileName    varchar(255), required

FileExtension       varchar(16), required

ContentType         varchar(255), required

FileSizeBytes       long, required

BlobETag            varchar(255), optional/recommended

UploadedBy          Guid, required

UploadedByName      varchar(200), required

SubmissionNote      varchar(2000), optional

SubmittedAt         DateTimeOffset, required

IsLate              bool, required

\`\`\`



Constraints:



\`\`\`text

UNIQUE(SubmissionId, VersionNumber)

UNIQUE(BlobName)

\`\`\`



The row is immutable after successful creation except for technically necessary corrections such as a storage integrity fix performed administratively. Normal business APIs must never mutate version content metadata.



**## 7.4 SubmissionReview**



Recommended fields:



\`\`\`text

Id                  Guid PK

SubmissionId        Guid, required

VersionId           Guid, required

Decision            enum/string, required

Feedback            text/varchar(10000), optional or required by decision

ReviewedBy          Guid, required

ReviewedByName      varchar(200), required

ReviewedAt          DateTimeOffset, required

\`\`\`



Rules:



\`\`\`text

APPROVED            feedback optional

CHANGES_REQUESTED   feedback required

REJECTED            feedback required

\`\`\`



Recommended unique constraint:



\`\`\`text

UNIQUE(VersionId)

\`\`\`



One completed version receives at most one formal review decision in the Sprint 4 design.



If a decision needs to be changed later, that should be a separately designed administrative workflow rather than silently overwriting review history.



**## 7.5 SubmissionComment**



Recommended fields:



\`\`\`text

Id                  Guid PK

SubmissionId        Guid, required

VersionId           Guid?, optional

AuthorId            Guid, required

AuthorName          varchar(200), required

AuthorRole          varchar(32), required

Comment             varchar/text, required (recommended max 4000)

CreatedAt           DateTimeOffset, required

\`\`\`



Sprint 4 comments are append-only.



No edit/delete/threading requirement for Sprint 4 unless explicitly added later.



Comments do not change submission status.



**## 7.6 SubmissionUploadSession**



Recommended fields:



\`\`\`text

Id                       Guid PK

ProjectId                Guid, required

RequirementId            Guid, required

SubmissionId             Guid, required/planned

VersionId                Guid, required/planned

ExpectedVersionNumber    int, required

BlobName                 varchar(1024), required

OriginalFileName         varchar(255), required

FileExtension            varchar(16), required

ExpectedContentType      varchar(255), required

ExpectedMaxFileSizeBytes long, required

CreatedBy                Guid, required

CreatedByName            varchar(200), required

Status                   enum/string, required

ExpiresAt                DateTimeOffset, required

CreatedAt                DateTimeOffset, required

CompletedAt              DateTimeOffset?, nullable

FailureReason            varchar(1000)?, optional

\`\`\`



Upload sessions are temporary security/business records.



They are not versions until completion succeeds.



\---



**# 8. Submission lifecycle state machine**



**## 8.1 Initial submission**



\`\`\`text

Requirement OPEN

&#x20;     │

&#x20;     │ no submission exists

&#x20;     │ Student completes V1 upload

&#x20;     ▼

ResearchSubmission created

Status = PENDING_REVIEW

CurrentVersion = V1

VersionCount = 1

\`\`\`



**## 8.2 Supervisor review**



\`\`\`text

PENDING_REVIEW

&#x20;    │

&#x20;    ├── APPROVED ───────────────► APPROVED

&#x20;    │

&#x20;    ├── CHANGES_REQUESTED ──────► CHANGES_REQUESTED

&#x20;    │

&#x20;    └── REJECTED ───────────────► REJECTED

\`\`\`



**## 8.3 Resubmission**



Only \`CHANGES_REQUESTED\` permits a normal new version.



\`\`\`text

CHANGES_REQUESTED

&#x20;     │

&#x20;     │ Student completes V2 upload

&#x20;     ▼

PENDING_REVIEW

CurrentVersion = V2

VersionCount += 1

\`\`\`



**## 8.4 Locked states**



\`\`\`text

PENDING_REVIEW  → no additional Student version

APPROVED        → locked

REJECTED        → locked

\`\`\`



This prevents the Supervisor reviewing V2 while the Student silently creates V3.



\---



**# 9. Meaning of review decisions**



**## APPROVED**



The reviewed version is accepted.



Backend updates:



\`\`\`text

Submission.Status = APPROVED

Submission.ApprovedVersionId = reviewed VersionId

Submission.ApprovedAt = now

\`\`\`



The submission becomes read-only for normal Student upload actions.



**## CHANGES_REQUESTED**



The reviewed version is not accepted yet, but revision is expected.



Feedback is mandatory.



Backend updates:



\`\`\`text

Submission.Status = CHANGES_REQUESTED

\`\`\`



Student may create exactly the next immutable version.



**## REJECTED**



Final rejection of the current logical submission under the current requirement.



Feedback is mandatory.



Backend updates:



\`\`\`text

Submission.Status = REJECTED

\`\`\`



Normal resubmission is locked.



If the product later needs a Supervisor “reopen rejected submission” operation, design it explicitly later. Do not silently make REJECTED behave like CHANGES_REQUESTED.



\---



**# 10. Due-date and late-submission rule**



Do not automatically block a late file solely because the due date passed.



On successful version completion:



\`\`\`text

IsLate = Requirement.DueAt != null && SubmittedAt > Requirement.DueAt

\`\`\`



This preserves academic evidence.



To truly stop uploads, Supervisor uses requirement \`CLOSED\`.



Late status is stored on each version because different versions may cross the due date differently.



\---



**# 11. Supported file policy**



Service-wide initial allowlist:



\`\`\`text

pdf



docx



pptx



zip

\`\`\`



Each requirement selects a subset.



Examples:



\`\`\`text

Final Thesis        → pdf, docx

Final Presentation  → pptx, pdf

Source Archive      → zip

\`\`\`



Recommended service configuration:



\`\`\`text

Submission__PlatformMaxFileSizeBytes

Submission__MaxFileNameLength

Submission__SupportedFileTypes__0=pdf

...

\`\`\`



Backend must enforce:



\- filename length,

\- extension allowlist,

\- declared MIME allowlist,

\- requirement-specific allowed types,

\- requirement-specific max size,

\- actual Azure blob size after upload,

\- actual Azure blob content type after upload.



Important limitation:



Extension and MIME validation do not prove that the binary content is safe. Malware/content inspection is outside Sprint 4 unless Defender for Storage or another scanning service is explicitly enabled.



\---



**# 12. Azure Blob Storage integration contract**

ResearchTrack Submission Management uses one Azure Blob Storage integration.

The Submission feature does not contain separate storage business flows for different environments. Provisioning, deployment topology, and credential delivery are infrastructure concerns and are intentionally outside this implementation contract.

The code-level design is:

```text
ResearchTrack Frontend
        │
        │ authenticated ResearchTrack API
        ▼
SubmissionService
        │
        ├── ProjectService authorization
        ├── submission business rules
        ├── MySQL state/metadata
        └── short-lived exact-blob SAS generation
                    │
                    ▼
          Azure Blob Storage
          research-submissions
          private container
```

**## 12.1 Required packages**

Add:

```text
Azure.Identity
Azure.Storage.Blobs
```

Keep versions consistent with repository package management.

**## 12.2 One private container**

Use:

```text
research-submissions
```

Rules:

```text
anonymous Blob access disabled
no permanent public Blob URLs
no container-wide access exposed to the browser
no SAS URL stored in MySQL
```

Azure stores file bytes only. MySQL remains the source of truth for requirements, submissions, versions, reviews, comments, status, uploader metadata and approved-version identity.

**## 12.3 SubmissionService is the authorization gate**

Azure does not decide ResearchTrack project membership.

Before generating any upload/read URL:

```text
authenticated user
      ↓
ProjectService access/manage check
      ↓
project + requirement/submission/version ownership check
      ↓
business-state validation
      ↓
short-lived SAS for one exact BlobName
```

If authorization fails:

```text
NO SAS URL is generated
```

**## 12.4 Storage credential boundary**

SubmissionService authenticates to Azure Storage through the configured Azure identity/credential.

The frontend must never receive:

```text
storage account key
connection string
Azure identity token
permanent storage credential
```

Credential selection stays inside infrastructure configuration and `AzureBlobStorageService`.

**## 12.5 SAS contract**

Upload SAS:

```text
one exact BlobName
short expiry
minimum create/write permissions
no list
no delete
no container-wide scope
```

Read/preview SAS:

```text
one exact BlobName
short expiry
read only
```

Where supported by the configured Azure identity, use a user delegation SAS.

Do not persist or log full SAS URLs.

**## 12.6 Blob naming**

Use opaque server-generated identity:

```text
projects/{projectId}/requirements/{requirementId}/submissions/{submissionId}/versions/{versionId}
```

Optionally append a normalized extension.

Original filename remains MySQL metadata.

Each `SubmissionVersion` receives its own BlobName. Resubmission never overwrites an existing Blob.

**## 12.7 Direct browser transfer**

Upload:

```text
Frontend
   ↓ request upload session
SubmissionService
   ↓ authorize + issue exact-blob write SAS
Frontend
   ↓ direct upload
Azure Blob Storage
   ↓
Frontend
   ↓ complete upload session
SubmissionService
   ↓ verify Blob + create immutable version
MySQL
```

Read/preview:

```text
Frontend
   ↓ request version URL
SubmissionService
   ↓ authorize + issue exact-blob read SAS
Frontend
   ↓ direct read/download
Azure Blob Storage
```

Normal file bodies are not proxied through Gateway/SubmissionService.

**## 12.8 URL lifetime**

SAS URLs are temporary operational values only.

Normal submission list/detail responses do not include usable permanent Blob URLs.

If a URL expires, the frontend requests a fresh URL from SubmissionService.

**# 13. Storage configuration**

Recommended options:

```text
BlobStorageOptions
```

Required configuration:

```text
Storage__ServiceUri=https://<account>.blob.core.windows.net
Storage__ContainerName=research-submissions
Storage__UploadSasExpiryMinutes=5
Storage__DownloadSasExpiryMinutes=5
```

Business/security limits remain separate:

```text
Submission__PlatformMaxFileSizeBytes=<value>
Submission__MaxFileNameLength=<value>
Submission__UploadSessionExpiryMinutes=10
Submission__UploadCleanupIntervalMinutes=60
```

Design rule:

```text
Storage options
    → Blob connectivity + temporary URL lifetime

Submission options
    → application/business validation
```

Do not place account keys, SAS tokens, user-specific Blob URLs or project-membership rules in application configuration.

**# 14. Azure storage abstraction**

Create:

```text
Infrastructure/Storage/
    IBlobStorageService.cs
    AzureBlobStorageService.cs
    StoredBlobProperties.cs
```

Recommended responsibilities:

```text
EnsureContainerReadyAsync()

CreateUploadUriAsync(
    blobName,
    contentType,
    expiresAt)

CreateReadUriAsync(
    blobName,
    expiresAt,
    downloadFileName?,
    disposition?)

GetBlobPropertiesAsync(blobName)

BlobExistsAsync(blobName)

DeleteBlobIfExistsAsync(blobName)
```

Do not expose Azure SDK types outside the infrastructure boundary if avoidable.

The rest of the application sees only:

```text
BlobName
temporary operation URL
StoredBlobProperties
```

The domain/application layer must never construct Azure Blob URLs itself.

**# 15. Upload-session flow**



This replaces the old SuperviseSuite \`upload-url\` + weak browser-supplied confirm metadata flow.



**## Phase 1 — create upload session**



Student calls:



\`\`\`text

POST /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/upload-sessions

\`\`\`



Request:



\`\`\`json

{

&#x20; "fileName": "thesis-v2.pdf",

&#x20; "contentType": "application/pdf",

&#x20; "fileSizeBytes": 8531142,

&#x20; "submissionNote": "Updated methodology section"

}

\`\`\`



Backend:



1\. authenticate user,

2\. verify Student role / project access,

3\. load requirement,

4\. require requirement OPEN,

5\. validate file metadata,

6\. determine whether this is V1 or a permitted resubmission,

7\. enforce submission status rule,

8\. calculate expected version number,

9\. create planned SubmissionId if first submission,

10\. create planned VersionId,

11\. generate opaque blob name,

12\. persist \`SubmissionUploadSession(PENDING)\`,

13\. generate short-lived Blob SAS for exactly that blob,

14\. return session metadata.



Response:



\`\`\`json

{

&#x20; "uploadSessionId": "...",

&#x20; "uploadUrl": "https\://...?...sas...",

&#x20; "expiresAt": "...",

&#x20; "versionNumber": 2,

&#x20; "maxFileSizeBytes": 20971520

}

\`\`\`



**## Phase 2 — direct browser upload**



Browser uploads bytes directly to Azure Blob Storage.



Application server does not proxy the file body.



**## Phase 3 — complete upload session**



Student calls:



\`\`\`text

POST /api/v1/projects/{projectId}/submissions/upload-sessions/{uploadSessionId}/complete

\`\`\`



Backend must **\*\*not trust browser-supplied final size/type\*\***.



Backend:



1\. reload session,

2\. verify current user owns session,

3\. verify project/requirement relationship,

4\. verify session is PENDING and not expired,

5\. read blob properties from Azure,

6\. require blob exists,

7\. require size > 0,

8\. require actual size <= required max,

9\. verify allowed content type against planned values/policy,

10\. calculate SubmittedAt/IsLate,

11\. transactionally create logical submission if first version,

12\. create immutable \`SubmissionVersion\`,

13\. update \`CurrentVersionId\`, \`VersionCount\`, \`LastSubmittedAt\`,

14\. set submission \`PENDING_REVIEW\`,

15\. mark upload session COMPLETED,

16\. return the new submission/version representation.



If final verification fails:



\- mark session FAILED where appropriate,

\- best-effort delete the blob if it should not remain,

\- do not create a SubmissionVersion.



\---



**# 16. Upload concurrency / idempotency**



The backend must protect against:



\- double-click upload-session creation,

\- two students trying first submission concurrently,

\- two students attempting resubmission concurrently,

\- repeated \`complete\` request,

\- stale session completed after Supervisor state changed.



Required safeguards:



1\. unique \`(ProjectId, RequirementId)\` ResearchSubmission constraint,

2\. unique \`(SubmissionId, VersionNumber)\` version constraint,

3\. one active/pending upload session per submission/expected version where practical,

4\. completion operation idempotency:

&#x20;  \- if session already COMPLETED, return existing completed version response rather than creating another version,

5\. re-check submission status at **\*\*completion time\*\***, not only upload-session creation time,

6\. transactional creation/update of Submission + Version + Session completion,

7\. conflict response if state changed unexpectedly.



\---



**# 17. Expired/incomplete upload cleanup**



Direct uploads can leave orphan blobs when the browser uploads successfully but never calls complete.



Implement a small cleanup path inside SubmissionService.



Recommended design:



\`\`\`text

ExpiredUploadSessionCleanupService : BackgroundService

\`\`\`



Configured low-frequency interval, for example every 60 minutes.



For sessions where:



\`\`\`text

Status = PENDING

ExpiresAt < now

\`\`\`



perform:



1\. mark EXPIRED,

2\. best-effort delete planned Blob if it exists,

3\. retain session row for short audit/troubleshooting period or delete later according to retention policy.



This is not a separate microservice.



\---



**# 18. Download / preview flow**



Frontend must never receive a permanent public blob URL.



Endpoint:



\`\`\`text

GET /api/v1/projects/{projectId}/submissions/{submissionId}/versions/{versionId}/download-url

\`\`\`



Optional query:



\`\`\`text

?disposition=inline

?disposition=attachment

\`\`\`



Backend:



1\. authenticate,

2\. authorize project access,

3\. verify submission/version belongs to project,

4\. generate read-only Blob SAS,

5\. return \`{ url, expiresAt }\`.



Use \`inline\` for PDF preview where supported.



Use \`attachment\` for explicit download.



\---



**# 19. Requirement APIs — Story 24**



Canonical API family:



\`\`\`text

GET    /api/v1/projects/{projectId}/submissions/requirements

POST   /api/v1/projects/{projectId}/submissions/requirements

GET    /api/v1/projects/{projectId}/submissions/requirements/{requirementId}

PATCH  /api/v1/projects/{projectId}/submissions/requirements/{requirementId}

DELETE /api/v1/projects/{projectId}/submissions/requirements/{requirementId}

POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/close

POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/reopen

POST   /api/v1/projects/{projectId}/submissions/requirements/{requirementId}/archive

\`\`\`



\`reopen\` is allowed for CLOSED if not ARCHIVED.



DELETE rule:



\`\`\`text

No ResearchSubmission exists → hard delete allowed.

History exists                → 409 Conflict; use archive.

\`\`\`



Supervisor management operations require ProjectService \`manage\` authorization.



Student may GET project requirements with ProjectService \`access\` authorization.



\---



**# 20. Submission query APIs — Stories 25–27**



Recommended endpoints:



\`\`\`text

GET /api/v1/projects/{projectId}/submissions

GET /api/v1/projects/{projectId}/submissions/{submissionId}

\`\`\`



The detail response should contain enough information to render:



\`\`\`text

requirement

current status

current version

version history

review per version

approved version marker

comments summary/count

late state

permissions / allowed actions if useful

\`\`\`



Do not return SAS URLs inside normal list/detail responses.



Generate storage URLs only on explicit preview/download request.



\---



**# 21. Review API — Story 26**



Endpoint:



\`\`\`text

POST /api/v1/projects/{projectId}/submissions/{submissionId}/reviews

\`\`\`



Request:



\`\`\`json

{

&#x20; "versionId": "...",

&#x20; "decision": "CHANGES_REQUESTED",

&#x20; "feedback": "Please correct Chapter 3 methodology and resubmit."

}

\`\`\`



Backend rules:



1\. require Supervisor role / project manage authorization,

2\. submission must belong to project,

3\. \`versionId\` must equal current version,

4\. submission status must be \`PENDING_REVIEW\`,

5\. version must have no existing formal review,

6\. \`CHANGES_REQUESTED\` / \`REJECTED\` require non-blank feedback,

7\. create immutable SubmissionReview,

8\. transition ResearchSubmission status,

9\. if APPROVED set ApprovedVersionId + ApprovedAt,

10\. return updated submission detail.



Reject stale review requests with 409 Conflict rather than approving an old version.



\---



**# 22. Comment APIs — Stories 26–27**



\`\`\`text

GET  /api/v1/projects/{projectId}/submissions/{submissionId}/comments

POST /api/v1/projects/{projectId}/submissions/{submissionId}/comments

\`\`\`



POST request:



\`\`\`json

{

&#x20; "versionId": "... optional ...",

&#x20; "comment": "Should the corrected diagram be included in Chapter 4?"

}

\`\`\`



Rules:



\- project access required,

\- Student or Supervisor may comment,

\- comment non-empty,

\- recommended max length 4000,

\- optional VersionId must belong to same submission,

\- comments append-only in Sprint 4,

\- comments do not affect formal status.



Sort comments oldest-first for conversation rendering.



\---



**# 23. Authorization matrix**



\| Operation | Student project member | Owning Supervisor |

\|---|---:|---:|

\| View requirements | Yes | Yes |

\| Create/edit/close/archive requirement | No | Yes |

\| Hard-delete unused requirement | No | Yes |

\| View submission/history | Yes | Yes |

\| Create V1 | Yes | No normal requirement (Supervisor can review, not submit as Student) |

\| Create next version | Yes, only CHANGES_REQUESTED | No |

\| View/download version | Yes | Yes |

\| Add comment | Yes | Yes |

\| Formal review | No | Yes |

\| Approve | No | Yes |

\| Request changes | No | Yes |

\| Reject | No | Yes |



Every operation also enforces project identity and entity ownership on the server.



Never depend on hidden frontend buttons as authorization.



\---



**# 24. User display-name resolution**



ResearchTrack JWT currently focuses on stable identity/role claims and should not be assumed to contain the complete display name.



Create/reuse:



\`\`\`text

IUserProfileClient

UserProfileClient

\`\`\`



Use the existing Auth/User endpoint such as \`/api/v1/users/me\` to snapshot:



\`\`\`text

CreatedByName

UploadedByName

ReviewedByName

AuthorName

\`\`\`



Snapshotting the display name is intentional: history should remain understandable even if a user later changes profile details.



A transient user-profile lookup failure should follow an explicit fallback strategy (for example email/user ID display fallback) rather than corrupting the submission transaction.



\---



**# 25. Suggested backend folder structure**



\`\`\`text

ResearchTrack.SubmissionService/



Configuration/

&#x20;   BlobStorageOptions.cs

&#x20;   SubmissionOptions.cs



Contracts/

&#x20;   Requirements/

&#x20;       CreateSubmissionRequirementRequest.cs

&#x20;       UpdateSubmissionRequirementRequest.cs

&#x20;       SubmissionRequirementResponse.cs

&#x20;   Submissions/

&#x20;       ResearchSubmissionListItemResponse.cs

&#x20;       ResearchSubmissionDetailResponse.cs

&#x20;       SubmissionVersionResponse.cs

&#x20;   Uploads/

&#x20;       CreateSubmissionUploadSessionRequest.cs

&#x20;       SubmissionUploadSessionResponse.cs

&#x20;       CompleteSubmissionUploadResponse.cs

&#x20;       SubmissionDownloadUrlResponse.cs

&#x20;   Reviews/

&#x20;       CreateSubmissionReviewRequest.cs

&#x20;       SubmissionReviewResponse.cs

&#x20;   Comments/

&#x20;       CreateSubmissionCommentRequest.cs

&#x20;       SubmissionCommentResponse.cs



Domain/

&#x20;   SubmissionRequirement.cs

&#x20;   ResearchSubmission.cs

&#x20;   SubmissionVersion.cs

&#x20;   SubmissionReview\.cs

&#x20;   SubmissionComment.cs

&#x20;   SubmissionUploadSession.cs

&#x20;   SubmissionRequirementStatus.cs

&#x20;   ResearchSubmissionStatus.cs

&#x20;   SubmissionReviewDecision.cs

&#x20;   SubmissionUploadSessionStatus.cs



Features/

&#x20;   Requirements/

&#x20;       ISubmissionRequirementService.cs

&#x20;       SubmissionRequirementService.cs

&#x20;   Submissions/

&#x20;       IResearchSubmissionService.cs

&#x20;       ResearchSubmissionService.cs

&#x20;   Uploads/

&#x20;       ISubmissionUploadService.cs

&#x20;       SubmissionUploadService.cs

&#x20;       ExpiredUploadSessionCleanupService.cs

&#x20;   Reviews/

&#x20;       ISubmissionReviewService.cs

&#x20;       SubmissionReviewService.cs

&#x20;   Comments/

&#x20;       ISubmissionCommentService.cs

&#x20;       SubmissionCommentService.cs



Infrastructure/

&#x20;   Authorization/

&#x20;       IProjectAuthorizationClient.cs

&#x20;       ProjectAuthorizationClient.cs

&#x20;   Identity/

&#x20;       IUserProfileClient.cs

&#x20;       UserProfileClient.cs

&#x20;   Storage/

&#x20;       IBlobStorageService.cs

&#x20;       AzureBlobStorageService.cs

&#x20;       StoredBlobProperties.cs



Persistence/

&#x20;   Configurations/

&#x20;       SubmissionRequirementConfiguration.cs

&#x20;       ResearchSubmissionConfiguration.cs

&#x20;       SubmissionVersionConfiguration.cs

&#x20;       SubmissionReviewConfiguration.cs

&#x20;       SubmissionCommentConfiguration.cs

&#x20;       SubmissionUploadSessionConfiguration.cs

&#x20;   Migrations/

&#x20;   SubmissionDbContext.cs

&#x20;   SubmissionPersistenceExtensions.cs



Controllers/

&#x20;   ProjectSubmissionRequirementsController.cs

&#x20;   ProjectSubmissionsController.cs

\`\`\`



If the repository convention prefers Controllers under Features, follow the existing project style, but keep these responsibilities separate.



\---



**# 26. Dependency injection / Program.cs plan**



Keep existing:



\`\`\`text

AddResearchTrackApi

AddResearchTrackJwtAuthentication

AddSubmissionPersistence

\`\`\`



Add extension methods such as:



\`\`\`text

AddSubmissionFeatures(configuration)

AddSubmissionStorage(configuration)

AddProjectAuthorizationClient(configuration)

AddUserProfileClient(configuration)

\`\`\`



Register:



\- options validation,

\- \`BlobServiceClient\`,

\- storage abstraction,

\- typed HttpClients,

\- requirement/submission/upload/review/comment services,

\- expired upload cleanup hosted service.



Startup should fail clearly if required storage configuration is missing/placeholder.



\---



**# 27. MySQL migration plan**



The current SubmissionService initial migration is essentially empty service scaffolding.



Create one feature migration after entity/configuration implementation.



Migration must create:



\`\`\`text

SubmissionRequirements

ResearchSubmissions

SubmissionVersions

SubmissionReviews

SubmissionComments

SubmissionUploadSessions

\`\`\`



Required integrity rules:



\`\`\`text

FK within SubmissionService only

ResearchSubmission -> SubmissionRequirement

SubmissionVersion -> ResearchSubmission

SubmissionReview -> ResearchSubmission

SubmissionReview -> SubmissionVersion

SubmissionComment -> ResearchSubmission

SubmissionComment -> SubmissionVersion nullable

UploadSession logical references as configured

\`\`\`



Cross-microservice IDs such as ProjectId/UserId remain GUID values without database FKs to other services.



Add unique/index constraints described above.



\---



**# 28. Transaction boundaries**



Use database transactions for operations that change multiple business records.



**## Complete upload**



Transaction:



\`\`\`text

create ResearchSubmission if V1

create SubmissionVersion

update ResearchSubmission current/version count/status

mark UploadSession COMPLETED

commit

\`\`\`



Azure blob verification happens before transaction.



If database commit fails after the blob exists, the blob becomes an orphan candidate for cleanup.



**## Formal review**



Transaction:



\`\`\`text

create SubmissionReview

update ResearchSubmission status

update approved-version fields if APPROVED

commit

\`\`\`



Comments can be individual atomic inserts.



\---



**# 29. Requirement mutation rules**



Supervisor may edit:



\- title,

\- description,

\- due date,

\- allowed file types,

\- maximum size,



but changes must not make historical versions invalid.



Recommended rule after a submission exists:



\- metadata such as title/description/due date may still be changed if product wants it,

\- allowed types/max size apply to future versions only,

\- existing completed versions remain valid history,

\- never retroactively delete a version because requirement rules changed.



If audit strictness is required later, add requirement-version snapshots; not required for current Sprint 4.



\---



**# 30. Response design**



Use stable IDs and explicit enums.



Example requirement response:



\`\`\`json

{

&#x20; "id": "...",

&#x20; "projectId": "...",

&#x20; "title": "Final Thesis",

&#x20; "description": "Submit the final thesis.",

&#x20; "dueAt": "2026-11-30T18:29:59Z",

&#x20; "allowedFileTypes": ["pdf", "docx"],

&#x20; "maxFileSizeBytes": 20971520,

&#x20; "status": "OPEN",

&#x20; "createdByName": "Supervisor Name",

&#x20; "createdAt": "...",

&#x20; "submissionSummary": {

&#x20;   "submissionId": "...",

&#x20;   "status": "CHANGES_REQUESTED",

&#x20;   "currentVersionNumber": 2,

&#x20;   "lastSubmittedAt": "..."

&#x20; }

}

\`\`\`



Example submission detail response should include versions ordered newest-first and review attached to each version.



\---



**# 31. HTTP/error semantics**



Follow existing ResearchTrack error conventions.



Recommended mapping:



\`\`\`text

400 Bad Request   malformed/validation

401 Unauthorized  missing/invalid auth

403 Forbidden     role/project authorization failure

404 Not Found     project-scoped entity not found

409 Conflict      invalid state transition / concurrency / history prevents delete

410 Gone          expired upload session if useful

503 Service Unavailable Azure/internal dependency unavailable where appropriate

\`\`\`



Do not leak:



\- storage credential detail,

\- SAS token in logs,

\- internal Azure exception internals to client.



\---



**# 32. Logging / observability**



Add structured logs/metrics for:



\`\`\`text

submission requirement create/update

upload session created

upload session expired

upload completion success/failure

Azure storage operation failure

submission version created

review decision

comment created

forbidden cross-project attempt

\`\`\`



Never log:



\`\`\`text

full SAS URLs

authorization headers

storage keys

file bytes

sensitive document content

\`\`\`



Prometheus metrics may include counts/durations but not document names or user PII as labels.



\---



**# 33. Blob CORS / direct-transfer infrastructure contract**

Because the browser transfers bytes directly to Blob Storage, Blob service CORS must permit the ResearchTrack frontend origins and the methods/headers used by the selected uploader.

This is an infrastructure prerequisite only.

The application design assumes:

```text
container remains private
anonymous access remains disabled
browser receives only short-lived exact-blob SAS
CORS does not grant authorization
SubmissionService remains the authorization gate
```

Do not place CORS or network-provisioning logic inside submission feature services/controllers.

**# 34. Storage data-protection recommendation**

Recommended Azure Blob settings:

```text
Private container
Anonymous access disabled
Blob soft delete enabled
Container soft delete enabled
```

Azure Blob versioning may optionally be enabled for infrastructure recovery.

It is not the ResearchTrack product version-history implementation.

ResearchTrack version history remains:

```text
SubmissionVersion rows in MySQL
+
one immutable BlobName per version
```

**# 35. Optional future malware scanning**



Do not make malware scanning a blocker for the current Sprint 4 unless the project explicitly budgets/enables it.



The design should not prevent later integration with Microsoft Defender for Storage or another scanner.



If later added, extend upload lifecycle with something like:



\`\`\`text

UPLOADED → SCANNING → CLEAN → PENDING_REVIEW

\`\`\`



That state is not required in the current implementation contract.



\---



**# 36. Story 24 backend implementation order**



**## 24.1 Domain + migration foundation**



Implement:



\- \`SubmissionRequirement\`,

\- requirement status enum,

\- EF configuration,

\- DbSet,

\- migration.



**## 24.2 Project authorization/profile infrastructure**



Implement/reuse:



\- \`IProjectAuthorizationClient\`,

\- \`IUserProfileClient\`.



**## 24.3 Requirement query + Supervisor commands**



Implement:



\- list/detail,

\- create,

\- patch,

\- close,

\- reopen,

\- archive,

\- hard delete only when unused.



**## 24.4 Tests**



Unit/integration tests for:



\- authorization,

\- validation,

\- status changes,

\- delete/archive rule,

\- project isolation.



\---



**# 37. Story 25 backend implementation order**



**## 25.1 Azure storage infrastructure**



Implement:



\- options,

\- BlobServiceClient,

\- configured Azure credential path,

\- user delegation SAS generation,

\- Blob property verification.



**## 25.2 Submission entities**



Implement:



\- ResearchSubmission,

\- SubmissionVersion,

\- SubmissionUploadSession,

\- mappings/indexes.



**## 25.3 Create upload session**



Implement V1 eligibility and metadata validation.



**## 25.4 Complete upload**



Implement Azure verification + transactional version creation.



**## 25.5 Download/preview SAS**



Implement project-authorized read SAS.



**## 25.6 Expired-session cleanup**



Implement background cleanup.



\---



**# 38. Story 26 backend implementation order**



**## 26.1 Review entity/service**



Implement:



\- SubmissionReview,

\- decision validation,

\- immutable review.



**## 26.2 Formal review endpoint**



Enforce:



\- manage authorization,

\- current-version requirement,

\- PENDING_REVIEW requirement,

\- feedback rules,

\- status transitions.



**## 26.3 Comment entity/service**



Implement Student/Supervisor append-only comments.



**## 26.4 Supervisor review queries**



Support list/filter for PENDING_REVIEW and complete detail history.



\---



**# 39. Story 27 backend implementation order**



**## 27.1 Resubmission eligibility**



Only allow new upload session when:



\`\`\`text

Requirement = OPEN

Submission = CHANGES_REQUESTED

\`\`\`



**## 27.2 Version number allocation**



Generate next version transactionally and enforce uniqueness.



**## 27.3 History response**



Return:



\- all versions,

\- submitter,

\- submitted time,

\- late flag,

\- formal review per version,

\- feedback,

\- approved/current markers,

\- comments.



**## 27.4 State regression tests**



Test the complete V1 → changes → V2 → approved lifecycle.



\---



**# 40. Backend automated test matrix**



At minimum add coverage for:



**## Requirement tests**



\- Supervisor create success,

\- Student create forbidden,

\- invalid allowed type,

\- invalid max size,

\- close/reopen/archive,

\- delete unused requirement,

\- delete used requirement conflict,

\- cross-project access.



**## Upload-session tests**



\- valid V1 session,

\- closed requirement blocked,

\- non-member blocked,

\- invalid file type,

\- too-large file,

\- PENDING_REVIEW blocks another version,

\- APPROVED/REJECTED blocks another version,

\- CHANGES_REQUESTED permits next version,

\- expired session blocked,

\- completed session idempotent.



**## Completion tests**



\- missing blob,

\- zero-size blob,

\- actual size too large,

\- wrong actual content type,

\- successful V1 creates submission/version,

\- late flag,

\- concurrency/duplicate completion.



**## Review tests**



\- approve success,

\- request-changes requires feedback,

\- reject requires feedback,

\- Student review forbidden,

\- stale version review conflict,

\- already reviewed version conflict,

\- wrong project forbidden/not found.



**## Comment tests**



\- Student comment,

\- Supervisor comment,

\- empty comment rejected,

\- wrong-version relation rejected,

\- non-member denied.



**## Storage tests**



Mock \`IBlobStorageService\`; do not require live Azure in normal unit tests.



Add a small optional integration test profile against the configured Azure Storage account if desired for deployment validation.



\---



**# 41. CI gate for backend**



Before Story completion:



\`\`\`text

dotnet restore

dotnet build ResearchTrack.sln -c Release

dotnet test ...

\`\`\`



Use the repository's existing backend CI entrypoints.



The new SubmissionService must not break unrelated service builds/tests.



\---



**# 42. Storage integration checklist**

Before Submission Management is considered integrated:

1. Azure Blob Storage is available.
2. Private `research-submissions` container exists.
3. Anonymous Blob access is disabled.
4. SubmissionService can authenticate to Blob Storage.
5. SubmissionService has the required Blob permissions.
6. `Storage__ServiceUri` and `Storage__ContainerName` are configured.
7. Short upload/read SAS expiries are configured.
8. Blob CORS supports the ResearchTrack browser transfer.
9. SubmissionService MySQL migration is applied.
10. Gateway submission routes are verified.
11. Upload-session → direct Blob upload → complete flow works.
12. Explicit preview/download URL generation works.
13. Cross-project users are denied before SAS generation.
14. SAS URLs are neither persisted nor logged.
15. Every completed version has a unique immutable BlobName.

**# 43. Explicit things we will NOT do**



Do not implement any of the following unless requirements change:



\- S3/AWS SDK in the final ResearchTrack SubmissionService,

\- permanent public Blob URLs,

\- storage account key exposed to frontend,

\- uploading the file through ResearchTrack Gateway/SubmissionService body proxy,

\- overwrite old submission files,

\- separate Student and Supervisor submission database tables,

\- separate SubmissionHistory table duplicating version data,

\- project membership tables in SubmissionService,

\- automatic deletion of late submissions,

\- editing an immutable old version,

\- Student formal approval/rejection,

\- automatic grading/productivity scoring,

\- mandatory malware scanner in current Sprint 4,

\- using Azure Blob versioning as the product's version-history UI.



\---



**# 44. Final backend completion definition**



The backend submission implementation is complete only when this end-to-end lifecycle works:



\`\`\`text

Supervisor creates Final Thesis requirement

&#x20;       ↓

Student requests upload session

&#x20;       ↓

SubmissionService authorizes and creates blob-scoped SAS

&#x20;       ↓

Browser uploads directly to private Azure Blob Storage

&#x20;       ↓

Student completes upload session

&#x20;       ↓

Backend verifies Azure blob and creates V1

&#x20;       ↓

Submission = PENDING_REVIEW

&#x20;       ↓

Supervisor previews/downloads V1

&#x20;       ↓

Supervisor REQUESTS CHANGES with feedback

&#x20;       ↓

Student creates V2

&#x20;       ↓

V1 remains immutable and downloadable

&#x20;       ↓

Submission returns to PENDING_REVIEW

&#x20;       ↓

Supervisor APPROVES V2

&#x20;       ↓

ApprovedVersionId = V2

&#x20;       ↓

Submission locked as APPROVED

&#x20;       ↓

Both roles can view full requirement/version/review/comment history

\`\`\`



If this lifecycle is preserved, implementation stays aligned with the finalized Sprint 4 design.



\---



**# 45. Official Azure reference points used by this design**



Use current Microsoft Learn documentation during implementation for exact SDK calls and Azure configuration:



\- User delegation SAS for Blob Storage (.NET):

&#x20; https\://learn.microsoft.com/azure/storage/blobs/storage-blob-user-delegation-sas-create-dotnet

\- Create a user delegation SAS / RBAC requirements:

&#x20; https\://learn.microsoft.com/rest/api/storageservices/create-user-delegation-sas

\- Authorize Blob access with Microsoft Entra ID / managed identity:

&#x20; https\://learn.microsoft.com/azure/storage/blobs/authorize-access-azure-active-directory

\- Azure built-in Storage roles:

&#x20; https\://learn.microsoft.com/azure/role-based-access-control/built-in-roles/storage



**\*\*Implementation rule:\*\*** if Azure SDK APIs have changed by implementation time, adjust only the SDK-specific call syntax. Do not change the business/domain architecture in this document unless a real requirement changes.
