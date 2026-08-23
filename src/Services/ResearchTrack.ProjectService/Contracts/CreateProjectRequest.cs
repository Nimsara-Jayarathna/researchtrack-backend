namespace ResearchTrack.ProjectService.Contracts;

public sealed class CreateProjectRequest
{
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Batch { get; init; }
    public string? Semester { get; init; }
    public IReadOnlyList<Guid>? StudentIds { get; init; }
    public Guid? LeaderStudentId { get; init; }
    public IReadOnlyList<CreateProjectMilestoneRequest>? Milestones { get; init; }
}

public sealed class CreateProjectMilestoneRequest
{
    public string? Title { get; init; }
    public string? Description { get; init; }
    public DateOnly? DueDate { get; init; }
}
