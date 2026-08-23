namespace ResearchTrack.ProjectService.Domain;

public sealed class ProjectMilestone
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public DateOnly DueDate { get; set; }
    public required string Status { get; set; }
    public int SequenceNo { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
