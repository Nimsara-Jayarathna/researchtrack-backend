namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectResponse(
    Guid Id,
    string Title,
    string Summary,
    string LifecycleStatus,
    string Batch,
    string Semester,
    int ProgressPercent,
    DateTime CreatedAt,
    DateTime UpdatedAt);
