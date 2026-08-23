namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectMilestoneResponse(
    Guid Id,
    string Title,
    string? Description,
    DateOnly DueDate,
    string Status,
    int SequenceNo);
