namespace ResearchTrack.ProjectService.Contracts;

public sealed class CreateProjectRequest
{
    public string? Title { get; init; }
    public string? Summary { get; init; }
    public string? Batch { get; init; }
    public string? Semester { get; init; }
}
