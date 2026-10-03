namespace ResearchTrack.SubmissionService.Domain;

public sealed class SubmissionReview
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid VersionId { get; set; }
    public string Decision { get; set; } = string.Empty;
    public string? Feedback { get; set; }
    public Guid ReviewedBy { get; set; }
    public string ReviewedByName { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAt { get; set; }
}
