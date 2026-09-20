using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Infrastructure;
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

        // Jira should expose at most one active sprint for a Scrum board. If malformed/stale
        // synchronized data contains more than one, prefer the most recently started sprint
        // on the linked board instead of failing the project view.
        var active = sprints
            .Where(x => string.Equals(x.State, "active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.StartDate)
            .ThenByDescending(x => x.JiraSprintId)
            .FirstOrDefault();

        JiraCurrentSprintResponse? current = null;
        if (active is not null)
        {
            var rows = await (
                from link in db.JiraIssueSprints.AsNoTracking()
                join issue in db.JiraIssues.AsNoTracking() on link.JiraIssueId equals issue.Id
                where link.JiraSprintId == active.Id && issue.ResearchProjectId == projectId
                select new { issue.StatusCategoryKey, issue.StoryPoints }
            ).ToListAsync(ct);

            var toDo = rows.Count(x => IsToDo(x.StatusCategoryKey));
            var inProgress = rows.Count(x => IsInProgress(x.StatusCategoryKey));
            var done = rows.Count(x => IsDone(x.StatusCategoryKey));
            // Unknown/custom category values are intentionally counted as remaining work.
            // They remain part of Total while not being incorrectly reported as Done.
            var total = rows.Count;
            var remaining = Math.Max(0, total - done);
            var completionPercent = total == 0 ? 0 : (int)Math.Round(100d * done / total, MidpointRounding.AwayFromZero);
            var pointsAvailable = rows.Any(x => x.StoryPoints.HasValue);
            var pointsTotal = rows.Where(x => x.StoryPoints.HasValue).Sum(x => x.StoryPoints ?? 0m);
            var pointsDone = rows.Where(x => IsDone(x.StatusCategoryKey) && x.StoryPoints.HasValue).Sum(x => x.StoryPoints ?? 0m);

            current = new JiraCurrentSprintResponse(
                active.JiraSprintId,
                active.Name,
                active.State,
                active.Goal,
                active.StartDate,
                active.EndDate,
                active.CompleteDate,
                new JiraSprintStatusBreakdownResponse(toDo, inProgress, done),
                total,
                done,
                remaining,
                completionPercent,
                pointsAvailable,
                pointsTotal,
                pointsDone);
        }

        return new JiraSprintProgressResponse(
            current is not null,
            current,
            new JiraSyncStateResponse(connection.SyncStatus, connection.LastSyncedAt, connection.LastSyncError));
    }

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
