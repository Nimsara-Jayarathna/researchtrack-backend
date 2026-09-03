namespace ResearchTrack.ProjectService.Contracts;

public sealed record SupervisorDashboardProjectResponse(
    Guid Id,
    string Title,
    string Summary,
    string LifecycleStatus,
    DateOnly? MilestoneDate,
    DateTime? LastActivityAt,
    int ProgressPercent,
    int MemberCount,
    string JiraHealthIndicator);
