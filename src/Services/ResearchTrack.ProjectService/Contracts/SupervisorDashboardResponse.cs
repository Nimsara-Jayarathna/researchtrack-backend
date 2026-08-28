namespace ResearchTrack.ProjectService.Contracts;

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
    IReadOnlyList<SupervisorDashboardProjectResponse> Projects,
    IReadOnlyList<SupervisorDashboardProjectResponse> RecentProjects);
