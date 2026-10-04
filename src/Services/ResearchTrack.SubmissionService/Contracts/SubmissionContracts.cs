namespace ResearchTrack.SubmissionService.Contracts;

public sealed record SubmissionRequirementCreateRequest(
    string? Title,
    string? Description,
    DateTimeOffset? DueAt,
    IReadOnlyList<string>? AllowedFileTypes,
    long MaxFileSizeBytes,
    string? ResponsibilityMode,
    Guid? AssignedStudentId);

public sealed record SubmissionRequirementUpdateRequest(
    string? Title,
    string? Description,
    DateTimeOffset? DueAt,
    IReadOnlyList<string>? AllowedFileTypes,
    long MaxFileSizeBytes,
    string? ResponsibilityMode,
    Guid? AssignedStudentId);

public sealed record SubmissionSummaryResponse(
    Guid Id,
    string Status,
    int VersionCount,
    int? CurrentVersionNumber,
    DateTimeOffset LastSubmittedAt);

public sealed record SubmissionResponsibilityResponse(
    string Mode,
    Guid? AssignedStudentId,
    string? AssignedStudentName,
    Guid? ResponsibleStudentId,
    string? ResponsibleStudentName,
    string? ResponsibleStudentRole,
    bool RequiresAssignment);

public sealed record SubmissionRequirementResponse(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    DateTimeOffset? DueAt,
    IReadOnlyList<string> AllowedFileTypes,
    long MaxFileSizeBytes,
    string Status,
    Guid CreatedBy,
    string CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    SubmissionResponsibilityResponse Responsibility,
    SubmissionSummaryResponse? SubmissionSummary);

public sealed record CreateUploadSessionRequest(
    string? FileName,
    string? ContentType,
    long FileSizeBytes,
    string? SubmissionNote);

public sealed record SubmissionUploadSessionResponse(
    Guid UploadSessionId,
    string UploadUrl,
    DateTimeOffset ExpiresAt,
    int VersionNumber,
    long MaxFileSizeBytes,
    IReadOnlyDictionary<string, string> RequiredHeaders);

public sealed record SubmissionReviewResponse(
    Guid Id,
    Guid SubmissionId,
    Guid VersionId,
    string Decision,
    string? Feedback,
    Guid ReviewedBy,
    string ReviewedByName,
    DateTimeOffset ReviewedAt);

public sealed record SubmissionVersionResponse(
    Guid Id,
    Guid SubmissionId,
    int VersionNumber,
    string OriginalFileName,
    string FileExtension,
    string ContentType,
    long FileSizeBytes,
    Guid UploadedBy,
    string UploadedByName,
    string? SubmitterRoleSnapshot,
    string? ResponsibilityModeSnapshot,
    string? SubmissionNote,
    DateTimeOffset SubmittedAt,
    bool IsLate,
    bool IsCurrent,
    bool IsApproved,
    SubmissionReviewResponse? Review);

public sealed record SubmissionRequirementSummaryResponse(
    Guid Id,
    string Title,
    string? Description,
    DateTimeOffset? DueAt,
    IReadOnlyList<string> AllowedFileTypes,
    long MaxFileSizeBytes,
    string Status,
    SubmissionResponsibilityResponse Responsibility);

public sealed record ResearchSubmissionResponse(
    Guid Id,
    Guid ProjectId,
    Guid RequirementId,
    string Status,
    int VersionCount,
    Guid CurrentVersionId,
    Guid? ApprovedVersionId,
    DateTimeOffset LastSubmittedAt,
    DateTimeOffset? ApprovedAt,
    SubmissionRequirementSummaryResponse Requirement,
    IReadOnlyList<SubmissionVersionResponse> Versions);

public sealed record SubmissionDownloadUrlResponse(
    string Url,
    DateTimeOffset ExpiresAt);

public sealed record CreateSubmissionReviewRequest(
    Guid VersionId,
    string? Decision,
    string? Feedback);
