using Microsoft.EntityFrameworkCore;
using ResearchTrack.ProjectService.Contracts;
using ResearchTrack.ProjectService.Domain;
using ResearchTrack.ProjectService.Persistence;

namespace ResearchTrack.ProjectService.Features.Dashboard;

public sealed class SupervisorDashboardService : ISupervisorDashboardService
{
    private const int UpcomingWindowDays = 14;
    private const int RecentProjectsLimit = 5;
    private const string NotConnected = "NOT_CONNECTED";

    private readonly IDbContextFactory<ProjectDbContext> _dbContextFactory;
    private readonly TimeProvider _timeProvider;

    public SupervisorDashboardService(
        IDbContextFactory<ProjectDbContext> dbContextFactory,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
    }

    public async Task<SupervisorDashboardResponse> GetAsync(
        Guid supervisorUserId,
        CancellationToken cancellationToken)
    {
        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var projects = await dbContext.Projects
            .AsNoTracking()
            .Where(project =>
                project.SupervisorUserId == supervisorUserId)
            .OrderByDescending(project => project.LastActivityAt)
            .ThenByDescending(project => project.CreatedAt)
            .Select(project =>
                new SupervisorDashboardProjectResponse(
                    project.Id,
                    project.Title,
                    project.Summary,
                    project.LifecycleStatus,
                    project.MilestoneDate,
                    project.LastActivityAt,
                    project.ProgressPercent,
                    dbContext.ProjectMembers.Count(member =>
                        member.ProjectId == project.Id),
                    NotConnected))
            .ToListAsync(cancellationToken);

        var today = DateOnly.FromDateTime(
            _timeProvider.GetUtcNow().UtcDateTime);
        var upcomingLimit = today.AddDays(UpcomingWindowDays);

        var totalProjects = projects.Count;
        var planningProjects = CountByStatus(
            projects,
            ProjectLifecycleStatuses.Planning);
        var activeProjects = CountByStatus(
            projects,
            ProjectLifecycleStatuses.Active);
        var atRiskProjects = CountByStatus(
            projects,
            ProjectLifecycleStatuses.AtRisk);
        var behindProjects = CountByStatus(
            projects,
            ProjectLifecycleStatuses.Behind);
        var completedProjects = CountByStatus(
            projects,
            ProjectLifecycleStatuses.Completed);

        var upcomingMilestonesCount = projects.Count(project =>
            project.LifecycleStatus != ProjectLifecycleStatuses.Completed &&
            project.MilestoneDate is { } milestoneDate &&
            milestoneDate >= today &&
            milestoneDate <= upcomingLimit);

        var recentProjects = projects
            .Take(RecentProjectsLimit)
            .ToArray();

        // Jira dashboard health is intentionally explicit while the project has
        // no Jira dashboard projection. Later Jira stories can enrich this read
        // model without making the Sprint 1 dashboard depend on Jira availability.
        const int jiraAtRiskCount = 0;
        const int jiraBehindCount = 0;

        return new SupervisorDashboardResponse(
            totalProjects,
            planningProjects,
            activeProjects,
            atRiskProjects,
            behindProjects,
            completedProjects,
            upcomingMilestonesCount,
            jiraAtRiskCount,
            jiraBehindCount,
            projects,
            recentProjects);
    }

    private static int CountByStatus(
        IEnumerable<SupervisorDashboardProjectResponse> projects,
        string lifecycleStatus) =>
        projects.Count(project =>
            string.Equals(
                project.LifecycleStatus,
                lifecycleStatus,
                StringComparison.OrdinalIgnoreCase));
}
