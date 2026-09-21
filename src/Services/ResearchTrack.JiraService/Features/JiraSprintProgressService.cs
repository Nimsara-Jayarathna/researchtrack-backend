using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraSprintProgressService
{
    Task<JiraSprintProgressResponse> GetAsync(Guid projectId, CancellationToken ct);
}

public sealed class JiraSprintProgressService : IJiraSprintProgressService
{
    private readonly IDbContextFactory<JiraDbContext> _factory;
    private readonly IProjectAuthorizationClient _authorization;

    public JiraSprintProgressService(IDbContextFactory<JiraDbContext> factory, IProjectAuthorizationClient authorization)
    {
        _factory = factory;
        _authorization = authorization;
    }

    public async Task<JiraSprintProgressResponse> GetAsync(Guid projectId, CancellationToken ct)
    {
        await _authorization.EnsureCanAccessAsync(projectId, ct);
        await using var db = await _factory.CreateDbContextAsync(ct);

        var connection = await db.JiraConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct)
            ?? throw new ApiException(404, ErrorCodes.NotFound, "Jira is not connected for this project.");

        var sprints = await db.JiraSprints.AsNoTracking()
            .Where(x => x.ResearchProjectId == projectId)
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.JiraSprintId)
            .ToListAsync(ct);

        // Build progress for every synchronized sprint. The endpoint is a local-store read:
        // no Jira API call is made while rendering the project view.
        // Do not translate a Guid[] Contains(...) predicate here. MySQL.EntityFrameworkCore 10 can
        // fail while binding the collection parameter (TypeMappedRelationalParameter) before SQL is
        // executed. Scope memberships through jira_sprints instead, using the same scalar projectId
        // predicate that is already known to work with this provider.
        List<SprintIssueRow> membershipRows = sprints.Count == 0
            ? new List<SprintIssueRow>()
            : await (
                from link in db.JiraIssueSprints.AsNoTracking()
                join sprint in db.JiraSprints.AsNoTracking() on link.JiraSprintId equals sprint.Id
                join issue in db.JiraIssues.AsNoTracking() on link.JiraIssueId equals issue.Id
                where sprint.ResearchProjectId == projectId && issue.ResearchProjectId == projectId
                select new SprintIssueRow(link.JiraSprintId, issue.StatusCategoryKey, issue.StoryPoints)
            ).ToListAsync(ct);

        var rowsBySprint = membershipRows
            .GroupBy(x => x.SprintId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<SprintIssueRow>)x.ToList());

        var progress = sprints
            .Select(sprint => BuildSprintProgress(
                sprint,
                rowsBySprint.TryGetValue(sprint.Id, out var rows) ? rows : Array.Empty<SprintIssueRow>()))
            .ToList();

        // Jira may support parallel active sprints. Keep the compatibility ActiveSprint field
        // deterministic while Sprints contains every active/future/closed sprint.
        var current = progress
            .Where(x => string.Equals(x.SprintState, "active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.SprintId)
            .FirstOrDefault();

        var summary = new JiraSprintSummaryResponse(
            progress.Count,
            progress.Count(x => string.Equals(x.SprintState, "active", StringComparison.OrdinalIgnoreCase)),
            progress.Count(x => string.Equals(x.SprintState, "future", StringComparison.OrdinalIgnoreCase)),
            progress.Count(x => string.Equals(x.SprintState, "closed", StringComparison.OrdinalIgnoreCase)));

        return new JiraSprintProgressResponse(
            current is not null,
            current,
            progress,
            summary,
            new JiraSyncStateResponse(connection.SyncStatus, connection.LastSyncedAt, connection.LastSyncError));
    }

    private static JiraCurrentSprintResponse BuildSprintProgress(
        JiraSprint sprint,
        IReadOnlyList<SprintIssueRow> rows)
    {
        var toDo = rows.Count(x => IsToDo(x.StatusCategoryKey));
        var inProgress = rows.Count(x => IsInProgress(x.StatusCategoryKey));
        var done = rows.Count(x => IsDone(x.StatusCategoryKey));
        var total = rows.Count;
        var remaining = Math.Max(0, total - done);
        var completionPercent = total == 0
            ? 0
            : (int)Math.Round(100d * done / total, MidpointRounding.AwayFromZero);
        var pointsAvailable = rows.Any(x => x.StoryPoints.HasValue);
        var pointsTotal = rows.Where(x => x.StoryPoints.HasValue).Sum(x => x.StoryPoints ?? 0m);
        var pointsDone = rows
            .Where(x => IsDone(x.StatusCategoryKey) && x.StoryPoints.HasValue)
            .Sum(x => x.StoryPoints ?? 0m);

        return new JiraCurrentSprintResponse(
            sprint.JiraSprintId,
            sprint.Name,
            sprint.State,
            sprint.Goal,
            sprint.StartDate,
            sprint.EndDate,
            sprint.CompleteDate,
            new JiraSprintStatusBreakdownResponse(toDo, inProgress, done),
            total,
            done,
            remaining,
            completionPercent,
            pointsAvailable,
            pointsTotal,
            pointsDone);
    }

    private sealed record SprintIssueRow(Guid SprintId, string? StatusCategoryKey, decimal? StoryPoints);

    private static bool IsDone(string? value) =>
        string.Equals(value, "done", StringComparison.OrdinalIgnoreCase);

    private static bool IsInProgress(string? value) =>
        string.Equals(value, "indeterminate", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "in_progress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "in progress", StringComparison.OrdinalIgnoreCase);

    private static bool IsToDo(string? value) =>
        string.Equals(value, "new", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "to_do", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "to do", StringComparison.OrdinalIgnoreCase);
}
