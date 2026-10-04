# ResearchTrack Sprint 4 — Meeting Management
## Backend Implementation Plan — FINAL IMPLEMENTATION CONTRACT

> **Status:** Frozen implementation plan for Stories 20–23  
> **Target service:** `ResearchTrack.MeetingService`  
> **Current database provider:** MySQL / EF Core  
> **Primary source of project authorization:** `ResearchTrack.ProjectService`  
> **Purpose:** This document is the implementation source of truth for the Sprint 4 Meeting Management backend. Do not redesign the feature during implementation unless the existing ResearchTrack architecture makes a specific item technically impossible.

---

# 1. Why this document exists

The current Sprint 4 ResearchTrack base already contains:

- a dedicated `ResearchTrack.MeetingService`;
- gateway routes reserved for the MeetingService;
- authentication and shared API building blocks;
- an empty `MeetingDbContext`;
- a fully developed meeting-oriented frontend largely derived from SuperviseSuite;
- separate Supervisor and Student meeting sections;
- existing frontend types for meeting channels and meeting records.

The original SuperviseSuite meeting feature provides the core business behavior that ResearchTrack should preserve:

1. Supervisors and Students work with the same project-scoped meeting data.
2. A Supervisor-created meeting channel is approved immediately.
3. A Student-created meeting channel is created as `PENDING`.
4. A Supervisor-created meeting record is approved immediately.
5. A Student-created meeting record is created as `PENDING`.
6. The owning Supervisor can approve Student-created channels and meeting records.
7. Students can view project meeting data but do not receive Supervisor management actions.
8. Project meeting history is represented by the MeetingRecord collection itself; it is not a separate history table.
9. A MeetingRecord may optionally reference a MeetingChannel.
10. Deleting a channel must not delete historical MeetingRecords.

ResearchTrack must preserve these behaviors while adapting them to the existing microservice architecture.

This document freezes:

- domain ownership;
- entity design;
- status model;
- Supervisor/Student permissions;
- project authorization;
- user-name snapshots;
- API routes;
- validation;
- sorting;
- persistence;
- transactional behavior;
- error semantics;
- migration order;
- service registration;
- testing;
- CI;
- deployment expectations;
- the implementation sequence for Stories 20–23.

---

# 2. Stories covered by this backend

## Story 20 — Supervisor Meeting Channel Management

Supervisor can:

- list all project meeting channels;
- create a channel;
- edit a channel;
- delete a channel;
- approve a Student-created `PENDING` channel;
- see channel status and submitter metadata.

Supervisor-created channels are immediately `APPROVED`.

---

## Story 21 — Supervisor Meeting History Management

Supervisor can:

- list complete project meeting history;
- create a supervision MeetingRecord;
- open record details;
- edit a record;
- delete a record;
- review Student-created `PENDING` records;
- approve a Student-created record;
- see creator and approval metadata.

Supervisor-created records are immediately `APPROVED`.

---

## Story 22 — Student Meeting Channel Management

Student can:

- list project meeting channels;
- open/copy channel links;
- submit a new meeting channel proposal;
- see whether the channel is `PENDING` or `APPROVED`.

Student-created channels are always `PENDING`.

Students cannot edit, delete or approve channels in Sprint 4.

---

## Story 23 — Student Meeting History Management

Student can:

- list project MeetingRecords;
- open record details;
- create a MeetingRecord;
- optionally associate it with a project MeetingChannel;
- see `PENDING` / `APPROVED` state;
- see approval metadata after Supervisor approval.

Student-created MeetingRecords are always `PENDING`.

Students cannot edit, delete or approve MeetingRecords in Sprint 4.

---

# 3. Current backend baseline that must be preserved

Current MeetingService:

```text
src/Services/ResearchTrack.MeetingService/

Configuration/
Contracts/
Domain/
Extensions/
Features/
Infrastructure/
Persistence/
    MeetingDbContext.cs
    MeetingDbContextFactory.cs
    MeetingPersistenceExtensions.cs
    Migrations/
Program.cs
ResearchTrack.MeetingService.csproj
appsettings.json
```

At the start of this Sprint 4 baseline:

- `MeetingDbContext` has no meeting DbSets;
- MeetingService has no meeting domain entities;
- MeetingService has no channel/record controllers;
- MeetingService already uses the common ResearchTrack API setup;
- MeetingService already uses common JWT authentication;
- MeetingService already uses MySQL EF Core persistence;
- Prometheus HTTP metrics are already enabled;
- the Gateway already reserves:

```text
/api/v1/projects/{projectId}/meetings/{**catch-all}
```

for the MeetingService.

These existing infrastructure conventions must be reused.

Do not create a second meeting microservice.

Do not put meeting persistence into ProjectService.

---

# 4. Non-negotiable architecture decisions

## 4.1 MeetingService owns meeting business state

MeetingService owns:

- `MeetingChannel`;
- `MeetingRecord`;
- channel approval state;
- record approval state;
- meeting validation;
- project meeting queries;
- meeting timestamps and user snapshots.

ProjectService does not own meeting rows.

AuthService does not own meeting rows.

---

## 4.2 ProjectService remains the project authorization source

MeetingService must not duplicate:

- projects;
- project Supervisors;
- project Student membership.

MeetingService calls the existing ProjectService authorization boundary.

Use the existing ProjectService endpoints:

```text
GET /api/v1/projects/{projectId}/authorization/access
GET /api/v1/projects/{projectId}/authorization/manage
```

Expected intent:

```text
access
  -> authenticated user is allowed to view/use the project

manage
  -> authenticated user is allowed to perform owning-Supervisor management actions
```

MeetingService must follow the same style already used by ResearchTrack services such as JiraService/GitHubService.

---

## 4.3 MeetingService has two persistent domains only

Do not create role-specific entities such as:

```text
SupervisorMeetingChannel
StudentMeetingChannel
SupervisorMeetingHistory
StudentMeetingHistory
StudentSubmittedMeetingRecord
```

Use only:

```text
MeetingChannel
MeetingRecord
```

Role and status determine behavior.

---

## 4.4 Meeting history is not a separate table

Story 21 and Story 23 both use `MeetingRecord`.

The meeting history is:

```text
project MeetingRecords
+
status
+
creator metadata
+
approval metadata
+
deterministic ordering
```

Do not create:

```text
MeetingHistory
MeetingHistoryItem
MeetingArchive
```

for Sprint 4.

---

## 4.5 Approval has two states only

Use:

```text
PENDING
APPROVED
```

Do not introduce:

```text
REJECTED
CHANGES_REQUESTED
```

into Meeting Management.

Those richer review decisions belong to the Submission feature, not the meeting feature.

---

## 4.6 Supervisor-created items auto-approve

When an authenticated Supervisor creates a MeetingChannel or MeetingRecord:

```text
AddedByRole = SUPERVISOR
Status = APPROVED
ApprovedBy = current user
ApprovedByName = current user's display snapshot
ApprovedAt = current UTC time
```

Do not force a Supervisor to approve their own meeting item afterward.

---

## 4.7 Student-created items always start pending

When an authenticated Student creates a MeetingChannel or MeetingRecord:

```text
AddedByRole = STUDENT
Status = PENDING
ApprovedBy = null
ApprovedByName = null
ApprovedAt = null
```

The client must not be able to override this.

Approval status is assigned by server business logic.

---

# 5. Final domain diagram

```text
Research Project
      │
      │ logical ProjectId reference
      │
      ├───────────────────────────────┐
      │                               │
      ▼                               ▼
MeetingChannel                  MeetingRecord
      │                               │
      │ 0..N                          │
      └───────────────────────────────┘
          optional ChannelId
```

Important:

- `ProjectId` is a logical cross-service identifier.
- Do not create a database foreign key from MeetingService to ProjectService.
- `MeetingRecord.ChannelId` is a local MeetingService relationship.
- `MeetingRecord.ChannelId` is nullable.
- deleting a MeetingChannel sets the local record reference to `NULL`.

---

# 6. Enumerations

## 6.1 MeetingApprovalStatus

```csharp
public enum MeetingApprovalStatus
{
    Pending = 0,
    Approved = 1
}
```

API serialization should remain compatible with the frontend contract:

```text
PENDING
APPROVED
```

Use one consistent string conversion approach throughout the service.

---

## 6.2 MeetingChannelPlatform

Supported Sprint 4 values:

```text
GOOGLE_MEET
ZOOM
TEAMS
WHATSAPP
OTHER
```

Recommended C# enum:

```csharp
public enum MeetingChannelPlatform
{
    GoogleMeet = 0,
    Zoom = 1,
    Teams = 2,
    WhatsApp = 3,
    Other = 4
}
```

API serialization must map to the existing uppercase frontend values.

Do not accept arbitrary platform strings.

---

## 6.3 MeetingActorRole

The persisted snapshot must support:

```text
SUPERVISOR
STUDENT
```

This can be:

- a small enum; or
- a validated string mapped from the authenticated role.

Do not persist arbitrary claim text.

---

# 7. MeetingChannel entity design

Recommended entity:

```text
MeetingChannel

Id                  Guid
ProjectId           Guid

Platform            MeetingChannelPlatform
ChannelName         string
LinkOrIdentifier    string

AddedBy             Guid
AddedByName         string
AddedByRole         MeetingActorRole

Status              MeetingApprovalStatus

ApprovedBy          Guid?
ApprovedByName      string?
ApprovedAt          DateTimeOffset?

CreatedAt           DateTimeOffset
UpdatedAt           DateTimeOffset?
```

Recommended constraints:

```text
ChannelName         required, max 255
LinkOrIdentifier    required, max 1024
AddedByName         required, max 200
ApprovedByName      nullable, max 200
```

Recommended indexes:

```text
IX_MeetingChannels_ProjectId
IX_MeetingChannels_ProjectId_Status
IX_MeetingChannels_ProjectId_CreatedAt
```

A composite index for the common project/status ordering is acceptable.

---

# 8. MeetingRecord entity design

Recommended entity:

```text
MeetingRecord

Id                  Guid
ProjectId           Guid

MeetingDate         DateOnly
DurationMinutes     int

DiscussionSummary   string
DiscussionDetails   string?

ChannelId           Guid?

AddedBy             Guid
AddedByName         string
AddedByRole         MeetingActorRole

Status              MeetingApprovalStatus

ApprovedBy          Guid?
ApprovedByName      string?
ApprovedAt          DateTimeOffset?

CreatedAt           DateTimeOffset
UpdatedAt           DateTimeOffset?
```

Recommended constraints:

```text
DurationMinutes       > 0
DiscussionSummary     required, max 1024
DiscussionDetails     nullable, max 5000
AddedByName           required, max 200
ApprovedByName        nullable, max 200
```

Recommended indexes:

```text
IX_MeetingRecords_ProjectId
IX_MeetingRecords_ProjectId_Status
IX_MeetingRecords_ProjectId_MeetingDate
IX_MeetingRecords_ChannelId
```

---

# 9. Local MeetingChannel relationship

MeetingRecord may reference a channel.

Relationship:

```text
MeetingChannel 1
     │
     │
     └── 0..N MeetingRecord
```

But `MeetingRecord.ChannelId` is optional.

Configure:

```text
OnDelete(DeleteBehavior.SetNull)
```

Result:

```text
delete MeetingChannel
        │
        ▼
MeetingRecord remains
ChannelId becomes null
```

This is mandatory because meeting history must survive channel deletion.

Never use cascade delete from MeetingChannel to MeetingRecord.

---

# 10. Time semantics

Use UTC for persisted timestamps:

```text
CreatedAt
UpdatedAt
ApprovedAt
```

Use `DateTimeOffset` or the established ResearchTrack UTC timestamp convention.

`MeetingDate` is a calendar date and should use `DateOnly` if supported consistently by the current MySQL provider.

The frontend receives:

```text
meetingDate: YYYY-MM-DD
createdAt: ISO-8601 timestamp
updatedAt: ISO-8601 timestamp or null
approvedAt: ISO-8601 timestamp or null
```

Do not silently convert `MeetingDate` to the server's local timezone.

---

# 11. User identity and display-name snapshot

The JWT already identifies the authenticated user and role.

Meeting rows must store stable user ID snapshots:

```text
AddedBy
ApprovedBy
```

The UI also expects:

```text
AddedByName
ApprovedByName
```

Recommended design:

```text
IUserProfileClient
UserProfileClient
```

Use the current authenticated user's profile endpoint, for example:

```text
GET /api/v1/users/me
```

for a display-name snapshot.

Important resilience rule:

- meeting creation/approval must not fail only because a display-name lookup failed;
- if profile lookup fails, fall back to a safe authenticated claim such as email;
- if email is unavailable, fall back to the stable user ID string.

The user ID is authoritative.

The display name is a convenience snapshot.

Do not perform an external user lookup every time a historic meeting row is returned.

---

# 12. Project authorization client

Create:

```text
Infrastructure/
    IProjectAuthorizationClient.cs
    ProjectAuthorizationClient.cs
```

Responsibilities:

```text
CanAccessProjectAsync(projectId, cancellationToken)
CanManageProjectAsync(projectId, cancellationToken)
```

or equivalent methods.

The internal HTTP client must:

- use the configured ProjectService base address;
- propagate the current bearer token where the existing ResearchTrack convention requires it;
- apply reasonable service timeout behavior;
- distinguish forbidden/not-found/upstream failure;
- never query ProjectService's database directly.

---

# 13. Authentication and role resolution

MeetingService continues to use:

```csharp
AddResearchTrackJwtAuthentication(...)
```

The business layer must derive the current actor from trusted server authentication.

Never accept:

```json
{
  "addedBy": "...",
  "addedByRole": "SUPERVISOR",
  "status": "APPROVED"
}
```

from the browser.

Those fields must not appear in create/update request DTOs.

Server-generated fields:

```text
Id
ProjectId
AddedBy
AddedByName
AddedByRole
Status
ApprovedBy
ApprovedByName
ApprovedAt
CreatedAt
UpdatedAt
```

---

# 14. Canonical MeetingService API

The canonical Sprint 4 API is project-centric.

Do not build new primary endpoints under:

```text
/api/supervisor/...
/api/student/...
```

Use:

```text
/api/v1/projects/{projectId}/meetings/...
```

The user's authenticated role controls business behavior.

This matches the gateway route already reserved for MeetingService.

---

# 15. Channel APIs

## 15.1 List project channels

```http
GET /api/v1/projects/{projectId}/meetings/channels
```

Authorization:

```text
project access
```

Allowed roles:

```text
SUPERVISOR
STUDENT
```

Response:

```json
[
  {
    "id": "...",
    "projectId": "...",
    "platform": "ZOOM",
    "channelName": "Weekly Research Meeting",
    "linkOrIdentifier": "https://zoom.us/...",
    "addedBy": "...",
    "addedByName": "Student Name",
    "addedByRole": "STUDENT",
    "status": "PENDING",
    "approvedBy": null,
    "approvedByName": null,
    "approvedAt": null,
    "createdAt": "...",
    "updatedAt": null
  }
]
```

Sort on the backend consistently:

```text
PENDING first
CreatedAt DESC
```

The frontend may also sort defensively.

---

## 15.2 Create project channel

```http
POST /api/v1/projects/{projectId}/meetings/channels
```

Authorization:

```text
project access
```

Request:

```json
{
  "platform": "ZOOM",
  "channelName": "Weekly Research Meeting",
  "linkOrIdentifier": "https://zoom.us/..."
}
```

Server logic:

```text
validate project access
validate role
validate payload
resolve current-user name snapshot

if SUPERVISOR:
    status = APPROVED
    approval = current user + now

if STUDENT:
    status = PENDING
    approval = null

save
return response
```

---

## 15.3 Update project channel

```http
PATCH /api/v1/projects/{projectId}/meetings/channels/{channelId}
```

Authorization:

```text
project manage
```

Supervisor only.

Request contains only:

```text
platform
channelName
linkOrIdentifier
```

Do not permit update request to change:

```text
AddedBy
AddedByRole
Status
ApprovedBy
ApprovedAt
CreatedAt
```

A Supervisor may edit a Student-created `PENDING` channel before approval.

Editing a channel does not implicitly approve it.

---

## 15.4 Delete project channel

```http
DELETE /api/v1/projects/{projectId}/meetings/channels/{channelId}
```

Authorization:

```text
project manage
```

Supervisor only.

Rules:

- channel must belong to `projectId`;
- remove the channel;
- linked MeetingRecords remain;
- linked MeetingRecords receive `ChannelId = null`.

Return:

```text
204 No Content
```

---

## 15.5 Approve project channel

```http
POST /api/v1/projects/{projectId}/meetings/channels/{channelId}/approve
```

Authorization:

```text
project manage
```

Supervisor only.

Allowed transition:

```text
PENDING -> APPROVED
```

On approval:

```text
Status = APPROVED
ApprovedBy = current Supervisor
ApprovedByName = snapshot
ApprovedAt = UTC now
UpdatedAt = UTC now
```

If already approved:

- reject the operation;
- do not silently treat it as another approval;
- use the common ResearchTrack validation/business error shape.

No `REJECT` operation in Sprint 4 Meeting Management.

---

# 16. MeetingRecord APIs

## 16.1 List project MeetingRecords

```http
GET /api/v1/projects/{projectId}/meetings/records
```

Authorization:

```text
project access
```

Allowed roles:

```text
SUPERVISOR
STUDENT
```

Ordering:

```text
1. PENDING first
2. MeetingDate DESC
3. CreatedAt DESC
```

This endpoint serves:

- Supervisor review list;
- Supervisor meeting history;
- Student submission status;
- Student meeting history.

No separate history endpoint is required.

---

## 16.2 Create MeetingRecord

```http
POST /api/v1/projects/{projectId}/meetings/records
```

Authorization:

```text
project access
```

Request:

```json
{
  "meetingDate": "2026-10-03",
  "durationMinutes": 45,
  "discussionSummary": "Reviewed chapter three methodology.",
  "discussionDetails": "Discussed sampling method and changes...",
  "channelId": "..."
}
```

Business logic:

```text
validate access
validate actor role
validate record fields

if ChannelId supplied:
    channel must exist
    channel must belong to projectId

resolve current-user name snapshot

if SUPERVISOR:
    status = APPROVED
    approval = current user + now

if STUDENT:
    status = PENDING
    approval = null

save
return
```

An associated channel does not need to be `APPROVED`.

It only needs to exist inside the same project.

This preserves the original SuperviseSuite business behavior.

---

## 16.3 Update MeetingRecord

```http
PATCH /api/v1/projects/{projectId}/meetings/records/{recordId}
```

Authorization:

```text
project manage
```

Supervisor only.

Mutable fields:

```text
MeetingDate
DurationMinutes
DiscussionSummary
DiscussionDetails
ChannelId
```

Do not change:

```text
AddedBy
AddedByName
AddedByRole
CreatedAt
```

Editing a pending Student MeetingRecord does not implicitly approve it.

---

## 16.4 Delete MeetingRecord

```http
DELETE /api/v1/projects/{projectId}/meetings/records/{recordId}
```

Authorization:

```text
project manage
```

Supervisor only.

Rules:

- record must belong to project;
- hard delete is acceptable for the current Sprint 4 design;
- no separate audit/version history is required.

Return:

```text
204 No Content
```

---

## 16.5 Approve MeetingRecord

```http
POST /api/v1/projects/{projectId}/meetings/records/{recordId}/approve
```

Authorization:

```text
project manage
```

Supervisor only.

Allowed transition:

```text
PENDING -> APPROVED
```

Set:

```text
Status
ApprovedBy
ApprovedByName
ApprovedAt
UpdatedAt
```

Repeated approval must fail safely.

---

# 17. Validation contract — MeetingChannel

Backend validation must be authoritative.

## Platform

Required.

Allowed values only:

```text
GOOGLE_MEET
ZOOM
TEAMS
WHATSAPP
OTHER
```

## ChannelName

Rules:

```text
required
trimmed
not whitespace-only
max 255
```

## LinkOrIdentifier

Although the inherited name is `linkOrIdentifier`, the current ResearchTrack UI treats it as a URL.

Freeze the Sprint 4 rule as:

```text
required
max 1024
absolute URL
scheme = http or https
```

Do not allow:

```text
javascript:
file:
data:
```

Do not accept empty identifiers.

The backend and frontend must use the same rule.

---

# 18. Validation contract — MeetingRecord

## MeetingDate

Required.

For Sprint 4:

- parse as calendar date;
- do not introduce a new future-date restriction unless the Jira acceptance criteria are changed.

## DurationMinutes

Required.

Rule:

```text
> 0
```

Do not invent a maximum duration unless the Product Owner adds one.

## DiscussionSummary

Rules:

```text
required
trimmed
not whitespace-only
max 1024
```

## DiscussionDetails

Rules:

```text
optional
trim if supplied
max 5000
```

## ChannelId

Rules:

```text
optional
must resolve to MeetingChannel
channel.ProjectId must equal route projectId
```

Do not allow a channel from another project.

---

# 19. Authorization matrix

| Operation | Supervisor | Student | Project authorization |
|---|---:|---:|---|
| List channels | Yes | Yes | Access |
| Create channel | Yes | Yes | Access |
| Update channel | Yes | No | Manage |
| Delete channel | Yes | No | Manage |
| Approve channel | Yes | No | Manage |
| List MeetingRecords | Yes | Yes | Access |
| Create MeetingRecord | Yes | Yes | Access |
| Update MeetingRecord | Yes | No | Manage |
| Delete MeetingRecord | Yes | No | Manage |
| Approve MeetingRecord | Yes | No | Manage |
| View record details | Yes | Yes | Access |

Server authorization is mandatory even if the frontend hides actions.

---

# 20. Student visibility

Students see the shared project meeting dataset.

For Sprint 4, do not filter the history down to "only records submitted by the current Student."

Students can see:

```text
Supervisor-created approved records
Student-created pending records
Student-created approved records
```

for the Research Project they belong to.

Likewise, Students may see project meeting channels including `PENDING` and `APPROVED` status.

This matches the existing UI/business model.

---

# 21. Status transition rules

## MeetingChannel

```text
Student create:
NONE -> PENDING

Supervisor create:
NONE -> APPROVED

Supervisor approve:
PENDING -> APPROVED
```

No transition:

```text
APPROVED -> PENDING
APPROVED -> APPROVED via approve
PENDING -> REJECTED
```

---

## MeetingRecord

```text
Student create:
NONE -> PENDING

Supervisor create:
NONE -> APPROVED

Supervisor approve:
PENDING -> APPROVED
```

No other Sprint 4 transition is defined.

---

# 22. Sorting contract

## Channels

Backend query:

```text
Status PENDING first
CreatedAt DESC
```

If desired, a stable final tie-breaker can use `Id`.

---

## MeetingRecords

Backend query:

```text
Status PENDING first
MeetingDate DESC
CreatedAt DESC
```

This makes the same endpoint useful for:

- Supervisor review;
- project history.

---

# 23. No pagination for Sprint 4

The inherited SuperviseSuite implementation returns complete project lists.

For Sprint 4, keep that behavior unless project scale testing proves it unacceptable.

Do not introduce pagination only on one role's API.

If pagination is added later, it should be a shared MeetingService contract change for both roles.

---

# 24. Response contracts

## MeetingChannelResponse

```csharp
public sealed record MeetingChannelResponse(
    Guid Id,
    Guid ProjectId,
    string Platform,
    string ChannelName,
    string LinkOrIdentifier,
    Guid AddedBy,
    string AddedByName,
    string AddedByRole,
    string Status,
    Guid? ApprovedBy,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
```

---

## MeetingRecordResponse

```csharp
public sealed record MeetingRecordResponse(
    Guid Id,
    Guid ProjectId,
    DateOnly MeetingDate,
    int DurationMinutes,
    string DiscussionSummary,
    string? DiscussionDetails,
    Guid? ChannelId,
    Guid AddedBy,
    string AddedByName,
    string AddedByRole,
    string Status,
    Guid? ApprovedBy,
    string? ApprovedByName,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
```

Keep the JSON field names compatible with the current TypeScript types.

---

# 25. Request contracts

## CreateMeetingChannelRequest

```text
Platform
ChannelName
LinkOrIdentifier
```

## UpdateMeetingChannelRequest

Same editable fields.

---

## CreateMeetingRecordRequest

```text
MeetingDate
DurationMinutes
DiscussionSummary
DiscussionDetails
ChannelId
```

## UpdateMeetingRecordRequest

Same editable fields.

Never expose approval or actor fields in request DTOs.

---

# 26. Recommended backend folder structure

```text
ResearchTrack.MeetingService/

Configuration/
    ProjectServiceOptions.cs
    UserProfileOptions.cs

Contracts/
    MeetingChannelResponse.cs
    MeetingRecordResponse.cs
    CreateMeetingChannelRequest.cs
    UpdateMeetingChannelRequest.cs
    CreateMeetingRecordRequest.cs
    UpdateMeetingRecordRequest.cs

Domain/
    MeetingChannel.cs
    MeetingRecord.cs
    MeetingApprovalStatus.cs
    MeetingChannelPlatform.cs
    MeetingActorRole.cs

Extensions/
    MeetingFeatureExtensions.cs

Features/
    MeetingChannels/
        IMeetingChannelService.cs
        MeetingChannelService.cs
        MeetingChannelMapper.cs

    MeetingRecords/
        IMeetingRecordService.cs
        MeetingRecordService.cs
        MeetingRecordMapper.cs

Infrastructure/
    IProjectAuthorizationClient.cs
    ProjectAuthorizationClient.cs
    IUserProfileClient.cs
    UserProfileClient.cs
    CurrentMeetingActor.cs
    CurrentMeetingActorResolver.cs

Persistence/
    MeetingDbContext.cs
    MeetingDbContextFactory.cs
    MeetingPersistenceExtensions.cs

    Configurations/
        MeetingChannelConfiguration.cs
        MeetingRecordConfiguration.cs

    Migrations/
        ...

Controllers/
    ProjectMeetingChannelsController.cs
    ProjectMeetingRecordsController.cs

Program.cs
```

Follow the current ResearchTrack folder/namespace conventions if controller placement differs.

The important point is separation of:

```text
HTTP contracts
domain
feature services
infrastructure clients
persistence
```

---

# 27. DbContext plan

Update:

```text
MeetingDbContext
```

to expose:

```csharp
public DbSet<MeetingChannel> MeetingChannels => Set<MeetingChannel>();
public DbSet<MeetingRecord> MeetingRecords => Set<MeetingRecord>();
```

Apply configurations through:

```csharp
modelBuilder.ApplyConfigurationsFromAssembly(...)
```

if that convention is already used.

Do not leave entity mapping implicit for critical length/index/delete behavior.

---

# 28. MySQL migration plan

Create one coherent Sprint 4 meeting migration after the entities/configurations are complete.

Recommended order:

```text
1. MeetingChannels
2. MeetingRecords
3. local ChannelId foreign key
4. indexes
```

No cross-service Project/User foreign keys.

The migration must apply cleanly to:

- a fresh MeetingService database;
- the current empty InitialCreate baseline.

The model snapshot must be updated.

---

# 29. Service registration

Add a feature registration extension, for example:

```csharp
builder.Services.AddMeetingFeatures(builder.Configuration);
```

Register:

```text
IMeetingChannelService
IMeetingRecordService

IProjectAuthorizationClient
IUserProfileClient
CurrentMeetingActorResolver

typed/named HttpClient(s)
```

Keep:

```text
AddResearchTrackApi
AddResearchTrackJwtAuthentication
AddMeetingPersistence
Prometheus metrics
```

unchanged.

---

# 30. Program.cs target shape

Conceptually:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddResearchTrackApi("ResearchTrack Meeting Service");
builder.Services.AddResearchTrackJwtAuthentication(builder.Configuration);
builder.Services.AddMeetingPersistence(builder.Configuration);
builder.Services.AddMeetingFeatures(builder.Configuration);

var app = builder.Build();

app.UseHttpMetrics();
app.UseResearchTrackApi();

app.MapMetrics();

app.Run();
```

Do not duplicate common exception/auth middleware already provided by BuildingBlocks.

---

# 31. Configuration

MeetingService needs explicit service-to-service configuration.

Example:

```json
{
  "Services": {
    "Project": {
      "BaseUrl": ""
    },
    "Auth": {
      "BaseUrl": ""
    }
  }
}
```

Use the repository's established environment naming convention rather than inventing a conflicting one.

Required deployment configuration should include:

```text
Meeting DB connection
ProjectService URL
AuthService/User-profile URL
JWT shared configuration
```

No additional third-party integration credentials are required for Meeting Management.

---

# 32. Transaction boundaries

## Create MeetingChannel

One database transaction is sufficient for:

```text
construct entity
set actor/status/approval snapshot
insert
save
```

Authorization/profile lookup occurs before persistence.

---

## Approve MeetingChannel

Perform:

```text
load by ProjectId + ChannelId
validate PENDING
set approval fields
save
```

The state check and update must be in the same database unit of work.

---

## Create MeetingRecord

Perform:

```text
validate optional channel
construct entity
set actor/status/approval snapshot
insert
save
```

The channel relationship must be validated against the same route ProjectId.

---

## Approve MeetingRecord

Perform:

```text
load by ProjectId + RecordId
validate PENDING
set approval fields
save
```

Do not perform approval as a blind update by ID alone.

---

# 33. Cross-project object protection

Every entity mutation/query by object ID must also scope by ProjectId.

Wrong:

```text
MeetingChannels.FindAsync(channelId)
```

as the only boundary.

Preferred:

```text
SingleOrDefaultAsync(x =>
    x.Id == channelId &&
    x.ProjectId == projectId)
```

Same for MeetingRecord.

This prevents an authorized Supervisor of Project A from manipulating an ID belonging to Project B.

---

# 34. Error semantics

Use existing ResearchTrack ProblemDetails/error conventions.

Recommended behavior:

```text
400
    invalid input
    invalid business transition
    invalid linked channel

401
    no valid authentication

403
    authenticated but lacks project access/manage permission

404
    channel/record does not exist in the route project

500/502/503
    controlled service/infrastructure failure according to existing conventions
```

Repeated approval:

```text
400
Only pending meeting channels can be approved.

400
Only pending meeting records can be approved.
```

Do not leak internal exception text.

---

# 35. Logging and observability

MeetingService already emits Prometheus HTTP metrics.

Add structured logs for important operations:

```text
meeting channel created
meeting channel approved
meeting channel deleted
meeting record created
meeting record approved
meeting record deleted
ProjectService authorization failure
UserProfile lookup fallback
```

Include:

```text
ProjectId
ChannelId or RecordId
ActorId
Action
Outcome
```

Do not log:

- JWT tokens;
- full Authorization headers;
- unnecessary sensitive discussion text.

Discussion notes should not be dumped into normal logs.

---

# 36. Story 20 backend implementation order

## 20.1 Authorization/profile infrastructure

Implement:

```text
ProjectAuthorizationClient
UserProfileClient
CurrentMeetingActorResolver
```

Verify these independently.

---

## 20.2 MeetingChannel domain and migration

Implement:

```text
MeetingChannel
enums
EF configuration
DbSet
migration
```

---

## 20.3 Channel query/create

Implement:

```text
GET channels
POST channel
```

Verify Supervisor auto-approval and Student pending behavior.

---

## 20.4 Supervisor channel management

Implement:

```text
PATCH
DELETE
POST approve
```

Verify manage authorization and cross-project protection.

---

## 20.5 Story 20 backend tests

Story 20 is not complete until these automated tests pass.

---

# 37. Story 21 backend implementation order

## 21.1 MeetingRecord entity

Implement entity/configuration/migration update.

---

## 21.2 Record query/create

Implement:

```text
GET records
POST record
```

Validate optional channel ownership.

---

## 21.3 Supervisor update/delete

Implement:

```text
PATCH
DELETE
```

---

## 21.4 Student-record approval

Implement:

```text
POST approve
```

---

## 21.5 Ordering/history response

Ensure:

```text
PENDING
MeetingDate DESC
CreatedAt DESC
```

---

## 21.6 Story 21 backend tests

Cover CRUD, history, approval and channel deletion behavior.

---

# 38. Story 22 backend implementation order

Story 22 largely reuses Story 20 backend functionality.

Required completion work is to prove and lock Student behavior:

```text
project access can list channels
Student can create
server forces PENDING
Student cannot update
Student cannot delete
Student cannot approve
Student cannot spoof status/role
non-member is denied
```

Do not create new Student-specific domain or controller logic unless the common endpoint requires a small role branch.

---

# 39. Story 23 backend implementation order

Story 23 largely reuses Story 21 backend functionality.

Required completion work:

```text
Student can list project MeetingRecords
Student can create MeetingRecord
server forces PENDING
optional same-project channel works
Student cannot update
Student cannot delete
Student cannot approve
non-member is denied
history metadata is returned
```

No new Student history table.

---

# 40. Backend automated test matrix

## 40.1 MeetingChannel validation tests

Test:

```text
valid platforms
invalid platform
blank channel name
overlong channel name
valid http URL
valid https URL
invalid scheme
malformed URL
overlong URL
```

---

## 40.2 Supervisor MeetingChannel tests

Test:

```text
Supervisor create -> APPROVED
ApprovedBy = Supervisor
ApprovedAt set
list project channels
edit own project channel
delete own project channel
approve pending channel
approve already approved -> failure
cross-project channel update -> denied/not found
cross-project channel delete -> denied/not found
cross-project approve -> denied/not found
```

---

## 40.3 Student MeetingChannel tests

Test:

```text
Student list own project channels
Student create -> PENDING
Student cannot submit ApprovedBy/Status
Student update -> denied
Student delete -> denied
Student approve -> denied
Student non-member access -> denied
```

---

## 40.4 MeetingRecord validation tests

Test:

```text
valid record
missing date
duration 0
negative duration
blank summary
summary > 1024
details > 5000
null details
null ChannelId
same-project ChannelId
other-project ChannelId -> rejected
missing ChannelId target -> rejected
```

---

## 40.5 Supervisor MeetingRecord tests

Test:

```text
Supervisor create -> APPROVED
list history
update record
delete record
approve pending Student record
approval metadata correct
repeat approval fails
cross-project update/delete/approve denied
```

---

## 40.6 Student MeetingRecord tests

Test:

```text
Student list shared project history
Student create -> PENDING
Student create without channel
Student create with same-project channel
Student update -> denied
Student delete -> denied
Student approve -> denied
non-member list/create -> denied
```

---

## 40.7 Channel deletion history test

Critical regression:

```text
create channel
create record linked to channel
delete channel
record still exists
record.ChannelId == null
```

This test is mandatory.

---

## 40.8 Sorting tests

Channels:

```text
pending before approved
newer first within group
```

Records:

```text
pending before approved
meetingDate descending
createdAt descending tie-break
```

---

## 40.9 Authorization client tests

Test mapping of:

```text
access allowed
access denied
manage allowed
manage denied
ProjectService unavailable
invalid ProjectId
```

---

# 41. Integration/API tests

Where the ResearchTrack test infrastructure permits, add API-level tests for:

```text
JWT Supervisor -> create approved channel
JWT Student -> create pending channel
JWT Supervisor -> approve Student channel

JWT Supervisor -> create approved record
JWT Student -> create pending record
JWT Supervisor -> approve Student record

non-member -> 403
Student manager endpoint -> 403
cross-project object ID -> no unauthorized mutation
```

Do not rely only on service-unit tests.

---

# 42. CI gate for MeetingService

Before Story 20–23 code is considered complete, backend CI must pass:

```text
dotnet restore
dotnet build
dotnet test
```

plus any repository-specific:

```text
format/analyzer checks
migration checks
database verification
integration tests
```

The implementation must not break:

```text
AuthService
ProjectService
GitHubService
JiraService
Gateway
SubmissionService
```

---

# 43. Deployment / DevOps checklist

Required deployment work:

```text
[ ] MeetingService image includes new code.
[ ] Meeting database migration runs through the approved process.
[ ] Gateway MeetingService destination is configured.
[ ] ProjectService internal URL is configured.
[ ] Auth/User profile service URL is configured if required.
[ ] Shared JWT values are available exactly as other services use them.
[ ] Health endpoint reports healthy.
[ ] Prometheus endpoint remains available.
[ ] Production/test startup does not contain placeholder service URLs.
```

No extra external SaaS setup is needed.

---

# 44. Explicit things we will NOT do

Sprint 4 Meeting Management will not add:

```text
meeting participant attendance
calendar integration
Teams/Zoom API meeting creation
video/audio recording
meeting attachments
meeting action-item domain
next-meeting scheduler
notifications
meeting rejection workflow
meeting resubmission workflow
meeting version history
soft-delete audit table
academic scoring from meetings
separate Student/Supervisor meeting tables
separate MeetingHistory table
cross-service database joins
```

If any of these are later required, they must be new backlog scope.

---

# 45. Backend Definition of Done — Story 20

```text
[ ] MeetingChannel domain exists.
[ ] MeetingChannel migration applies.
[ ] Access/manage authorization uses ProjectService.
[ ] Supervisor create auto-approves.
[ ] Student create becomes PENDING.
[ ] Supervisor list/create/update/delete/approve APIs work.
[ ] URL validation matches frontend.
[ ] Cross-project manipulation is blocked.
[ ] Automated tests pass.
[ ] API/integration tests pass where supported.
[ ] CI passes.
```

---

# 46. Backend Definition of Done — Story 21

```text
[ ] MeetingRecord domain exists.
[ ] optional ChannelId relationship exists.
[ ] channel delete uses SET NULL behavior.
[ ] Supervisor create auto-approves.
[ ] Student create becomes PENDING.
[ ] Supervisor list/create/update/delete/approve APIs work.
[ ] record validation rules are enforced.
[ ] project meeting history ordering is correct.
[ ] approval metadata is persisted.
[ ] automated tests pass.
[ ] CI passes.
```

---

# 47. Backend Definition of Done — Story 22

```text
[ ] Student can list project channels.
[ ] Student can create a PENDING channel.
[ ] Student cannot spoof approval.
[ ] Student cannot edit/delete/approve channels.
[ ] non-member is denied.
[ ] status returned matches Supervisor view.
[ ] automated role/security tests pass.
```

---

# 48. Backend Definition of Done — Story 23

```text
[ ] Student can list shared project MeetingRecords.
[ ] Student can create a PENDING MeetingRecord.
[ ] optional same-project channel works.
[ ] Student cannot update/delete/approve records.
[ ] non-member is denied.
[ ] approval metadata is visible after Supervisor approval.
[ ] history remains readable after channel deletion.
[ ] automated role/security tests pass.
```

---

# 49. Final implementation sequence

Recommended implementation order:

```text
FOUNDATION
    ProjectAuthorizationClient
    UserProfileClient
    actor resolution
       │
       ▼
STORY 20
    MeetingChannel
    migration
    channel APIs
       │
       ▼
STORY 21
    MeetingRecord
    relationship
    record APIs
       │
       ▼
STORY 22
    Student channel security completion
       │
       ▼
STORY 23
    Student record/history security completion
       │
       ▼
REGRESSION
    backend tests
    cross-role tests
    cross-project tests
    migration verification
    CI
```

This avoids duplicating foundational work across four Jira stories.

---

# 50. Frozen backend business flow

```text
RESEARCH PROJECT
      │
      ├──────────── MEETING CHANNEL ──────────────┐
      │                                           │
      │ Supervisor creates -> APPROVED            │
      │ Student creates    -> PENDING             │
      │                         │                 │
      │                         └-> Supervisor approve
      │                                           │
      └──────────── MEETING RECORD ───────────────┘
                         │
              Supervisor creates -> APPROVED
              Student creates    -> PENDING
                                      │
                                      └-> Supervisor approve
                                                   │
                                                   ▼
                                     SHARED MEETING HISTORY
```

This state model is the final backend contract for Sprint 4 Meeting Management.

---

# 51. Implementation guardrails

During implementation:

1. Do not redesign the status model.
2. Do not create role-specific meeting database tables.
3. Do not bypass ProjectService authorization.
4. Do not put meeting state into ProjectService.
5. Do not introduce a new history table.
6. Do not add reject/resubmit logic to meetings.
7. Do not change the existing frontend response field names without updating the complete contract.
8. Do not allow client-supplied creator/approval fields.
9. Do not cascade-delete historical MeetingRecords when a channel is removed.
10. Do not claim Story completion until backend CI and role-boundary tests pass.

This README is the backend source of truth for the Meeting Management implementation.
