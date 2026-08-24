namespace ResearchTrack.ProjectService.Contracts;

public sealed record ProjectResponse(
    Guid Id,
    string Title,
    string Summary,
    string LifecycleStatus,
    string Batch,
    string Semester,
    DateOnly? MilestoneDate,
    DateTime? LastActivityAt,
    int ProgressPercent,
    ProjectUserResponse? Supervisor,
    ProjectUserResponse? Leader,
    IReadOnlyList<ProjectMemberResponse> Members,
    IReadOnlyList<ProjectMilestoneResponse> Milestones);
