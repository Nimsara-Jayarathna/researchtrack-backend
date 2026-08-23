namespace ResearchTrack.ProjectService.Contracts;

public sealed record CreateProjectResponse(
    Guid Id,
    string Title,
    string Summary,
    string Batch,
    string Semester,
    string LifecycleStatus,
    int ProgressPercent,
    DateOnly MilestoneDate,
    IReadOnlyList<ProjectUserResponse> Students,
    ProjectUserResponse? Leader,
    IReadOnlyList<ProjectMilestoneResponse> Milestones);
