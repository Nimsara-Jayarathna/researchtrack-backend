namespace ResearchTrack.ProjectService.Domain;

public sealed class Project
{
    public Guid Id { get; set; }
    public required string Title { get; set; }
    public required string Summary { get; set; }
    public required string Batch { get; set; }
    public required string Semester { get; set; }
    public required string LifecycleStatus { get; set; }
    public int ProgressPercent { get; set; }
    public Guid SupervisorUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
