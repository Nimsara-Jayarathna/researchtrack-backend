namespace ResearchTrack.ProjectService.Contracts;

public sealed record UpdateProjectMilestoneRequest(
    string Title,
    string? Description,
    DateOnly DueDate);