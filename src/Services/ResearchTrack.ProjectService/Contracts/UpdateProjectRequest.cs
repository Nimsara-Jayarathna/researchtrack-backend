namespace ResearchTrack.ProjectService.Contracts;

public sealed record UpdateProjectRequest(
    string Title,
    string? Summary,
    string Batch,
    string Semester,
    string? LifecycleStatus);