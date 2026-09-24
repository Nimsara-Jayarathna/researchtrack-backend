using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraWorkloadService
{
    Task<JiraWorkloadResponse> GetAsync(Guid projectId, CancellationToken ct);
}

public sealed class JiraWorkloadService : IJiraWorkloadService
{
    private readonly IDbContextFactory<JiraDbContext> _factory;
    private readonly IProjectAuthorizationClient _authorization;

    public JiraWorkloadService(IDbContextFactory<JiraDbContext> factory, IProjectAuthorizationClient authorization)
    {
        _factory = factory;
        _authorization = authorization;
    }

    public async Task<JiraWorkloadResponse> GetAsync(Guid projectId, CancellationToken ct)
    {
        await _authorization.EnsureCanAccessAsync(projectId, ct);
        await using var db = await _factory.CreateDbContextAsync(ct);

        var connection = await db.JiraConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct)
            ?? throw new ApiException(404, ErrorCodes.NotFound, "Jira is not connected for this project.");

        var issues = await db.JiraIssues.AsNoTracking()
            .Where(x => x.ResearchProjectId == projectId)
            .Select(x => new WorkloadIssue(
                x.IssueKey, x.Summary, x.StatusName, x.StatusCategoryKey,
                x.AssigneeAccountId, x.AssigneeDisplayName, x.StoryPoints,
                x.JiraUpdatedAt, x.SyncedAt))
            .ToListAsync(ct);

        // Workload is deliberately based on Jira's status category, not project-specific
        // status names. Current work means every synchronized issue that is not DONE.
        var members = issues
            .Where(x => !string.IsNullOrWhiteSpace(x.AssigneeAccountId) || !string.IsNullOrWhiteSpace(x.AssigneeDisplayName))
            .GroupBy(x => AssigneeKey(x.AssigneeAccountId, x.AssigneeDisplayName), StringComparer.Ordinal)
            .Select(group => BuildMember(group))
            .OrderByDescending(x => x.Active)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unassigned = issues.Where(x => string.IsNullOrWhiteSpace(x.AssigneeAccountId) && string.IsNullOrWhiteSpace(x.AssigneeDisplayName)).ToList();
        var unassignedActive = unassigned.Count(x => !IsDone(x.StatusCategoryKey));
        var unassignedDone = unassigned.Count - unassignedActive;

        var totalActive = issues.Count(x => !IsDone(x.StatusCategoryKey));
        var totalDone = issues.Count - totalActive;
        var assignedActive = issues.Count(x => !IsDone(x.StatusCategoryKey) &&
            (!string.IsNullOrWhiteSpace(x.AssigneeAccountId) || !string.IsNullOrWhiteSpace(x.AssigneeDisplayName)));

        return new JiraWorkloadResponse(
            members,
            new JiraUnassignedWorkloadResponse(unassigned.Count, unassignedActive, unassignedDone),
            new JiraWorkloadSummaryResponse(issues.Count, totalActive, totalDone, assignedActive, unassignedActive),
            new JiraSyncStateResponse(connection.SyncStatus, connection.LastSyncedAt, connection.LastSyncError));
    }

    private static JiraWorkloadMemberResponse BuildMember(IGrouping<string, WorkloadIssue> group)
    {
        var rows = group.ToList();
        var done = rows.Count(x => IsDone(x.StatusCategoryKey));
        var inProgress = rows.Count(x => IsInProgress(x.StatusCategoryKey));
        var active = rows.Count - done;
        var toDo = Math.Max(0, active - inProgress);
        var displayName = rows.Select(x => x.AssigneeDisplayName).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Unknown Jira user";
        var accountId = rows.Select(x => x.AssigneeAccountId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? group.Key;
        var pointsAvailable = rows.Any(x => x.StoryPoints.HasValue);
        var activePoints = pointsAvailable ? rows.Where(x => !IsDone(x.StatusCategoryKey)).Sum(x => x.StoryPoints ?? 0m) : (decimal?)null;

        return new JiraWorkloadMemberResponse(
            accountId,
            displayName,
            rows.Count,
            active,
            toDo,
            inProgress,
            done,
            activePoints,
            rows.OrderByDescending(x => x.UpdatedAt ?? x.SyncedAt)
                .Select(x => new JiraWorkloadIssueResponse(x.IssueKey, x.Summary, x.Status, x.StatusCategoryKey, IsDone(x.StatusCategoryKey)))
                .ToList());
    }

    private static string AssigneeKey(string? accountId, string? displayName) =>
        !string.IsNullOrWhiteSpace(accountId) ? $"id:{accountId}" : $"name:{displayName!.Trim().ToUpperInvariant()}";

    private static bool IsDone(string? value) => string.Equals(value, "done", StringComparison.OrdinalIgnoreCase);
    private static bool IsInProgress(string? value) =>
        string.Equals(value, "indeterminate", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "in_progress", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "in progress", StringComparison.OrdinalIgnoreCase);

    private sealed record WorkloadIssue(string IssueKey, string Summary, string Status, string? StatusCategoryKey,
        string? AssigneeAccountId, string? AssigneeDisplayName, decimal? StoryPoints, DateTimeOffset? UpdatedAt, DateTimeOffset SyncedAt);
}
