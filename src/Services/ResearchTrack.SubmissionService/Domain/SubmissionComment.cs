namespace ResearchTrack.SubmissionService.Domain;

public sealed class SubmissionComment
{
    public Guid Id { get; set; }
    public Guid SubmissionId { get; set; }
    public Guid? VersionId { get; set; }
    public Guid AuthorId { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorRole { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
