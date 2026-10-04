namespace ResearchTrack.SubmissionService.Domain;

public sealed class SubmissionVersion
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public int VersionNumber { get; set; }
    public string ObjectKey { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string FileExtension { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? ObjectETag { get; set; }
    public Guid UploadedBy { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public string? SubmitterRoleSnapshot { get; set; }
    public string? ResponsibilityModeSnapshot { get; set; }
    public string? SubmissionNote { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public bool IsLate { get; set; }
}
