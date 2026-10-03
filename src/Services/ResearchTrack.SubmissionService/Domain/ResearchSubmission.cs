namespace ResearchTrack.SubmissionService.Domain;

public sealed class ResearchSubmission
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Guid RequirementId { get; set; }
    public string Status { get; set; } = SubmissionConstants.SubmissionStatus.PendingReview;
    public Guid? CurrentVersionId { get; set; }
    public int VersionCount { get; set; }
    public DateTimeOffset LastSubmittedAt { get; set; }
    public Guid? ApprovedVersionId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
