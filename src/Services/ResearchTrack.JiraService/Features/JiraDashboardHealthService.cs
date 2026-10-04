using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraDashboardHealthService
{
    Task<JiraDashboardHealthResponse> GetAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken);
}

public sealed class JiraDashboardHealthService : IJiraDashboardHealthService
{
    private const int MaximumProjects = 100;
    private const string NotConnected = "NOT_CONNECTED";
    private const string Healthy = "HEALTHY";
    private const string AtRisk = "AT_RISK";
    private const string Behind = "BEHIND";

    private readonly IDbContextFactory<JiraDbContext> _dbContextFactory;
    private readonly IProjectAuthorizationClient _authorization;
    private readonly TimeProvider _timeProvider;

    public JiraDashboardHealthService(
        IDbContextFactory<JiraDbContext> dbContextFactory,
        IProjectAuthorizationClient authorization,
        TimeProvider timeProvider)
    {
        _dbContextFactory = dbContextFactory;
        _authorization = authorization;
        _timeProvider = timeProvider;
    }

    public async Task<JiraDashboardHealthResponse> GetAsync(
        IReadOnlyCollection<Guid> projectIds,
        CancellationToken cancellationToken)
    {
        var distinctProjectIds = projectIds
            .Where(projectId => projectId != Guid.Empty)
            .Distinct()
            .ToArray();

        if (distinctProjectIds.Length > MaximumProjects)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                $"At most {MaximumProjects} projects can be requested at once.");
        }

        if (distinctProjectIds.Length == 0)
        {
            return new JiraDashboardHealthResponse(0, 0, 0, []);
        }

        // The dashboard request is Supervisor-only, but each project is still
        // authorization-checked so callers cannot use this endpoint to probe
        // Jira state for projects they do not manage.
        await Task.WhenAll(distinctProjectIds.Select(projectId =>
            _authorization.EnsureCanManageAsync(
                projectId,
                cancellationToken)));

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var projects = new List<JiraDashboardProjectHealthResponse>(
            distinctProjectIds.Length);

        // MySql.EntityFrameworkCore has had unstable translation around
        // parameterized multi-Guid Contains predicates in this solution.
        // Use single-project equality queries here; the endpoint is capped and
        // avoids reintroducing the same provider failure seen in SubmissionService.
        foreach (var projectId in distinctProjectIds)
        {
            var connection = await dbContext.JiraConnections
                .AsNoTracking()
                .Where(item => item.ResearchProjectId == projectId)
                .Select(item => new
                {
                    item.SyncStatus,
                    item.LastSyncedAt
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (connection is null)
            {
                projects.Add(new JiraDashboardProjectHealthResponse(
                    projectId,
                    false,
                    NotConnected,
                    0,
                    0,
                    0,
                    0,
                    null,
                    null));
                continue;
            }

            var issues = await dbContext.JiraIssues
                .AsNoTracking()
                .Where(item => item.ResearchProjectId == projectId)
                .Select(item => new
                {
                    item.StatusCategoryKey,
                    item.PriorityName,
                    item.DueDate
                })
                .ToListAsync(cancellationToken);

            var done = issues.Count(item => IsDone(item.StatusCategoryKey));
            var openIssues = issues.Count - done;
            var overdueIssues = issues.Count(item =>
                !IsDone(item.StatusCategoryKey) &&
                item.DueDate.HasValue &&
                item.DueDate.Value < now);
            var highPriorityOpen = issues.Count(item =>
                !IsDone(item.StatusCategoryKey) &&
                IsHighPriority(item.PriorityName));
            var completionPercent = issues.Count == 0
                ? 0
                : (int)Math.Round(100d * done / issues.Count);

            var indicator = DeriveIndicator(
                connection.SyncStatus,
                overdueIssues,
                highPriorityOpen);

            projects.Add(new JiraDashboardProjectHealthResponse(
                projectId,
                true,
                indicator,
                completionPercent,
                openIssues,
                overdueIssues,
                highPriorityOpen,
                connection.SyncStatus,
                connection.LastSyncedAt));
        }

        return new JiraDashboardHealthResponse(
            projects.Count(project => project.Connected),
            projects.Count(project => project.Indicator == AtRisk),
            projects.Count(project => project.Indicator == Behind),
            projects);
    }

    private static string DeriveIndicator(
        string syncStatus,
        int overdueIssues,
        int highPriorityOpen)
    {
        if (string.Equals(
                syncStatus,
                "INVALID_AUTH",
                StringComparison.OrdinalIgnoreCase) ||
            overdueIssues > 5 ||
            highPriorityOpen > 3)
        {
            return Behind;
        }

        if (string.Equals(
                syncStatus,
                "FAILED",
                StringComparison.OrdinalIgnoreCase) ||
            overdueIssues > 0 ||
            highPriorityOpen > 0)
        {
            return AtRisk;
        }

        return Healthy;
    }

    private static bool IsDone(string? statusCategoryKey) =>
        string.Equals(
            statusCategoryKey,
            "done",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsHighPriority(string? priorityName) =>
        string.Equals(
            priorityName,
            "Highest",
            StringComparison.OrdinalIgnoreCase) ||
        string.Equals(
            priorityName,
            "High",
            StringComparison.OrdinalIgnoreCase);
}
