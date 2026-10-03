namespace ResearchTrack.SubmissionService.Domain;

public sealed class SubmissionUploadSession
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequirementId { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid VersionId { get; set; }
    public int ExpectedVersionNumber { get; set; }
    public string TemporaryObjectKey { get; set; } = string.Empty;
    public string FinalObjectKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string ExpectedContentType { get; set; } = string.Empty;
    public long DeclaredFileSizeBytes { get; set; }
    public long ExpectedMaxFileSizeBytes { get; set; }
    public string? SubmissionNote { get; set; }
    public Guid CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string Status { get; set; } = SubmissionConstants.UploadSessionStatus.Pending;
    public string? ActiveSlot { get; set; } = "ACTIVE";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }
}
