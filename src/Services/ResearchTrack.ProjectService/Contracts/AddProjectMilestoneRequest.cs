namespace ResearchTrack.ProjectService.Contracts;

public sealed record AddProjectMilestoneRequest(
    string Title,
    string? Description,
    DateOnly DueDate);