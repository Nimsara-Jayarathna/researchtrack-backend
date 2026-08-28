namespace ResearchTrack.ProjectService.Contracts;

public sealed record SupervisorDashboardProjectItem(
    Guid Id,
    string Title,
    string? Summary,
    string LifecycleStatus,
    DateOnly? MilestoneDate,
    DateTime? LastActivityAt,
    int? ProgressPercent,
    string? JiraHealthIndicator);

public sealed record SupervisorDashboardResponse(
    int TotalProjects,
    int PlanningProjects,
    int ActiveProjects,
    int AtRiskProjects,
    int BehindProjects,
    int CompletedProjects,
    int UpcomingMilestonesCount,
    int JiraAtRiskCount,
    int JiraBehindCount,
    IReadOnlyList<SupervisorDashboardProjectItem> Projects,
    IReadOnlyList<SupervisorDashboardProjectItem> RecentProjects);