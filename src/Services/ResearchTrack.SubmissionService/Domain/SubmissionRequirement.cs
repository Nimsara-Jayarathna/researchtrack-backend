namespace ResearchTrack.SubmissionService.Domain;

public sealed class SubmissionRequirement
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset? DueAt { get; set; }
    public string AllowedFileTypes { get; set; } = string.Empty;
    public long MaxFileSizeBytes { get; set; }
    public string Status { get; set; } = SubmissionConstants.RequirementStatus.Open;
    public Guid CreatedBy { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
