using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Infrastructure;
using ResearchTrack.JiraService.Persistence;

namespace ResearchTrack.JiraService.Features;

public interface IJiraSyncService
{
    Task<JiraSyncResponse> SynchronizeAsync(Guid projectId, CancellationToken ct);
}

public sealed class JiraSyncService : IJiraSyncService
{
    private readonly IDbContextFactory<JiraDbContext> _factory;
    private readonly AtlassianClient _client;
    private readonly IJiraTokenProtector _protector;
    private readonly ILogger<JiraSyncService> _logger;

    public JiraSyncService(IDbContextFactory<JiraDbContext> factory, AtlassianClient client, IJiraTokenProtector protector, ILogger<JiraSyncService> logger)
    {
        _factory = factory;
        _client = client;
        _protector = protector;
        _logger = logger;
    }

    public async Task<JiraSyncResponse> SynchronizeAsync(Guid projectId, CancellationToken ct)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var connection = await db.JiraConnections.SingleOrDefaultAsync(x => x.ResearchProjectId == projectId, ct)
            ?? throw new ApiException(400, ErrorCodes.ValidationError, "Jira is not connected for this project.");

        // SYNCING describes the attempt. LastSyncedAt deliberately remains the timestamp of
        // the last fully successful snapshot, so readers can continue to use stale-good data.
        connection.SyncStatus = "SYNCING";
        connection.LastSyncError = null;
        connection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        try
        {
            var token = await ValidTokenAsync(db, connection, ct);

            // Phase 1: build the complete remote snapshot BEFORE changing mirrored data.
            // A Jira/HTTP failure here leaves the previous local snapshot untouched.
            var fields = await _client.GetFieldsAsync(token, connection.CloudId, ct);
            var storyPointsField = fields.FirstOrDefault(x =>
                string.Equals(x.Name, "Story Points", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.CustomSchema, "com.pyxis.greenhopper.jira:jsw-story-points", StringComparison.OrdinalIgnoreCase))?.Id;

            var remoteIssues = await _client.GetProjectIssuesAsync(
                token, connection.CloudId, connection.JiraProjectKey, storyPointsField, ct);

            IReadOnlyList<JsonElement> remoteSprints = Array.Empty<JsonElement>();
            var sprintMembers = new Dictionary<long, IReadOnlyList<string>>();

            // Re-resolve board metadata on every synchronization. Board selection is persisted
            // during linking, but Jira remains the authority for board type and a connection
            // created without an explicit board still needs a deterministic Scrum context.
            var boards = await _client.GetBoardsAsync(token, connection.CloudId, connection.JiraProjectKey, ct);
            JiraBoardOption? sprintBoard = null;
            if (connection.JiraBoardId.HasValue)
                sprintBoard = boards.FirstOrDefault(x => x.Id == connection.JiraBoardId.Value);

            if (sprintBoard is not null)
            {
                connection.JiraBoardName = sprintBoard.Name;
                connection.JiraBoardType = sprintBoard.Type;
            }
            else
            {
                // The stored board can disappear or older connections may not have selected one.
                // Prefer an available Scrum board so current-sprint data does not silently vanish.
                sprintBoard = boards
                    .Where(x => string.Equals(x.Type, "scrum", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.Id)
                    .FirstOrDefault();
                if (sprintBoard is not null)
                {
                    connection.JiraBoardId = sprintBoard.Id;
                    connection.JiraBoardName = sprintBoard.Name;
                    connection.JiraBoardType = sprintBoard.Type;
                }
            }

            var supportsSprints = sprintBoard is not null &&
                string.Equals(sprintBoard.Type, "scrum", StringComparison.OrdinalIgnoreCase);

            if (supportsSprints)
            {
                remoteSprints = await _client.GetSprintsAsync(token, connection.CloudId, sprintBoard!.Id, ct);
                foreach (var sprint in remoteSprints)
                {
                    if (!sprint.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var sprintId))
                        continue;
                    sprintMembers[sprintId] = await _client.GetSprintIssueIdsAsync(token, connection.CloudId, sprintId, ct);
                }
            }

            _logger.LogInformation(
                "Jira snapshot for project {ProjectId}: {IssueCount} issues, {BoardCount} boards, sprint board {BoardId} ({BoardType}), {SprintCount} sprints, {MembershipCount} sprint memberships.",
                projectId, remoteIssues.Count, boards.Count, sprintBoard?.Id, sprintBoard?.Type ?? "none", remoteSprints.Count, sprintMembers.Values.Sum(x => x.Count));

            var now = DateTimeOffset.UtcNow;
            var seenIssueIds = new HashSet<string>(StringComparer.Ordinal);
            var seenSprintIds = new HashSet<long>();

            // Phase 2: publish the validated snapshot atomically. Destructive reconciliation
            // only happens after all required remote reads have completed successfully.
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var existingIssues = await db.JiraIssues
                .Where(x => x.ResearchProjectId == projectId)
                .ToDictionaryAsync(x => x.JiraIssueId, ct);

            foreach (var raw in remoteIssues)
            {
                var jiraId = S(raw, "id");
                var key = S(raw, "key");
                if (jiraId is null || key is null || !raw.TryGetProperty("fields", out var issueFields))
                    continue;

                seenIssueIds.Add(jiraId);
                if (!existingIssues.TryGetValue(jiraId, out var issue))
                {
                    issue = new JiraIssue
                    {
                        Id = Guid.NewGuid(),
                        JiraConnectionId = connection.Id,
                        ResearchProjectId = projectId,
                        JiraIssueId = jiraId
                    };
                    db.JiraIssues.Add(issue);
                    existingIssues[jiraId] = issue;
                }
                MapIssue(issue, key, issueFields, storyPointsField, now);
            }

            var staleIssues = existingIssues.Values.Where(x => !seenIssueIds.Contains(x.JiraIssueId)).ToList();
            if (staleIssues.Count > 0)
            {
                var staleIds = staleIssues.Select(x => x.Id).ToList();
                db.JiraIssueSprints.RemoveRange(db.JiraIssueSprints.Where(x => staleIds.Contains(x.JiraIssueId)));
                db.JiraIssues.RemoveRange(staleIssues);
            }
            await db.SaveChangesAsync(ct);

            var issueIds = await db.JiraIssues
                .Where(x => x.ResearchProjectId == projectId)
                .ToDictionaryAsync(x => x.JiraIssueId, x => x.Id, ct);

            var projectIssueIds = issueIds.Values.ToList();
            if (projectIssueIds.Count > 0)
                db.JiraIssueSprints.RemoveRange(db.JiraIssueSprints.Where(x => projectIssueIds.Contains(x.JiraIssueId)));

            var existingSprints = await db.JiraSprints
                .Where(x => x.ResearchProjectId == projectId)
                .ToDictionaryAsync(x => x.JiraSprintId, ct);

            if (supportsSprints)
            {
                foreach (var raw in remoteSprints)
                {
                    if (!raw.TryGetProperty("id", out var idElement) || !idElement.TryGetInt64(out var sprintId))
                        continue;

                    seenSprintIds.Add(sprintId);
                    if (!existingSprints.TryGetValue(sprintId, out var sprint))
                    {
                        sprint = new JiraSprint
                        {
                            Id = Guid.NewGuid(),
                            JiraConnectionId = connection.Id,
                            ResearchProjectId = projectId,
                            JiraSprintId = sprintId,
                            JiraBoardId = sprintBoard!.Id
                        };
                        db.JiraSprints.Add(sprint);
                        existingSprints[sprintId] = sprint;
                    }

                    sprint.Name = S(raw, "name") ?? $"Sprint {sprintId}";
                    sprint.State = S(raw, "state") ?? "unknown";
                    sprint.Goal = S(raw, "goal");
                    sprint.StartDate = D(raw, "startDate");
                    sprint.EndDate = D(raw, "endDate");
                    sprint.CompleteDate = D(raw, "completeDate");
                    sprint.SyncedAt = now;

                    // New sprints need their generated local id before membership rows are added.
                    await db.SaveChangesAsync(ct);
                    if (!sprintMembers.TryGetValue(sprintId, out var members))
                        continue;
                    foreach (var jiraIssueId in members.Distinct(StringComparer.Ordinal))
                        if (issueIds.TryGetValue(jiraIssueId, out var localIssueId))
                            db.JiraIssueSprints.Add(new JiraIssueSprint { JiraIssueId = localIssueId, JiraSprintId = sprint.Id });
                }

                var staleSprints = existingSprints.Values.Where(x => !seenSprintIds.Contains(x.JiraSprintId)).ToList();
                if (staleSprints.Count > 0)
                    db.JiraSprints.RemoveRange(staleSprints);
            }
            else
            {
                // A selected non-Scrum board has no meaningful sprint snapshot. Clear any old
                // sprint data left from a previous board selection only after the issue fetch succeeded.
                if (existingSprints.Count > 0)
                    db.JiraSprints.RemoveRange(existingSprints.Values);
            }

            connection.SyncStatus = "SYNCED";
            connection.LastSyncedAt = now;
            connection.LastSyncError = null;
            connection.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return new JiraSyncResponse(seenIssueIds.Count, seenSprintIds.Count, now);
        }
        catch (Exception ex)
        {
            // Never move LastSyncedAt on failure. It identifies the last complete, usable snapshot.
            // Clear the tracker so a failed transaction cannot leak partially tracked mirror changes
            // into the failure-status write.
            db.ChangeTracker.Clear();
            var failedConnection = await db.JiraConnections.SingleAsync(x => x.ResearchProjectId == projectId, CancellationToken.None);
            failedConnection.SyncStatus = IsAuthorizationFailure(ex) ? "INVALID_AUTH" : "FAILED";
            failedConnection.LastSyncError = Safe(ex.Message);
            failedConnection.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }

    private async Task<string> ValidTokenAsync(JiraDbContext db, JiraConnection connection, CancellationToken ct)
    {
        if (!connection.TokenExpiresAt.HasValue || connection.TokenExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(2))
            return _protector.Unprotect(connection.AccessTokenProtected);

        if (string.IsNullOrWhiteSpace(connection.RefreshTokenProtected))
            throw new ApiException(401, ErrorCodes.Unauthorized, "Jira authorization has expired. Reconnect Jira.");

        var refreshed = await _client.RefreshTokenAsync(_protector.Unprotect(connection.RefreshTokenProtected), ct);
        connection.AccessTokenProtected = _protector.Protect(refreshed.AccessToken);
        if (!string.IsNullOrWhiteSpace(refreshed.RefreshToken))
            connection.RefreshTokenProtected = _protector.Protect(refreshed.RefreshToken);
        connection.TokenExpiresAt = refreshed.ExpiresAt;
        connection.Scope = refreshed.Scope ?? connection.Scope;
        await db.SaveChangesAsync(ct);
        return refreshed.AccessToken;
    }

    private static bool IsAuthorizationFailure(Exception ex) =>
        ex is ApiException api && api.StatusCode == 401;

    private static void MapIssue(JiraIssue x, string key, JsonElement f, string? sp, DateTimeOffset now)
    {
        x.IssueKey = key;
        x.Summary = S(f, "summary") ?? "(No summary)";
        x.DescriptionJson = f.TryGetProperty("description", out var desc) && desc.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? desc.GetRawText() : null;
        if (f.TryGetProperty("issuetype", out var it)) { x.IssueTypeId = S(it, "id"); x.IssueTypeName = S(it, "name") ?? "Unknown"; x.IsSubtask = B(it, "subtask"); }
        if (f.TryGetProperty("status", out var st)) { x.StatusId = S(st, "id"); x.StatusName = S(st, "name") ?? "Unknown"; if (st.TryGetProperty("statusCategory", out var sc)) { x.StatusCategoryId = S(sc, "id"); x.StatusCategoryKey = S(sc, "key"); x.StatusCategoryName = S(sc, "name"); } }
        if (f.TryGetProperty("priority", out var pr) && pr.ValueKind == JsonValueKind.Object) { x.PriorityId = S(pr, "id"); x.PriorityName = S(pr, "name"); } else { x.PriorityId = null; x.PriorityName = null; }
        Person(f, "assignee", out var aa, out var an); x.AssigneeAccountId = aa; x.AssigneeDisplayName = an;
        Person(f, "reporter", out var ra, out var rn); x.ReporterAccountId = ra; x.ReporterDisplayName = rn;
        if (f.TryGetProperty("parent", out var pa) && pa.ValueKind == JsonValueKind.Object) { x.ParentIssueId = S(pa, "id"); x.ParentIssueKey = S(pa, "key"); } else { x.ParentIssueId = null; x.ParentIssueKey = null; }
        if (f.TryGetProperty("resolution", out var re) && re.ValueKind == JsonValueKind.Object) { x.ResolutionId = S(re, "id"); x.ResolutionName = S(re, "name"); } else { x.ResolutionId = null; x.ResolutionName = null; }
        x.DueDate = D(f, "duedate"); x.ResolutionDate = D(f, "resolutiondate"); x.JiraCreatedAt = D(f, "created"); x.JiraUpdatedAt = D(f, "updated");
        if (f.TryGetProperty("timetracking", out var tt) && tt.ValueKind == JsonValueKind.Object) { x.OriginalEstimateSeconds = L(tt, "originalEstimateSeconds"); x.RemainingEstimateSeconds = L(tt, "remainingEstimateSeconds"); x.TimeSpentSeconds = L(tt, "timeSpentSeconds"); }
        if (!string.IsNullOrWhiteSpace(sp) && f.TryGetProperty(sp, out var spe) && spe.ValueKind == JsonValueKind.Number && spe.TryGetDecimal(out var n)) x.StoryPoints = n; else x.StoryPoints = null;
        x.SyncedAt = now;
    }

    private static void Person(JsonElement f, string name, out string? id, out string? display) { id = null; display = null; if (f.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Object) { id = S(p, "accountId"); display = S(p, "displayName"); } }
    private static string? S(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool B(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.True;
    private static long? L(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.TryGetInt64(out var v) ? v : null;
    private static DateTimeOffset? D(JsonElement e, string n) => e.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(p.GetString(), out var d) ? d : null;
    private static string Safe(string m) => m.Length <= 1000 ? m : m[..1000];
}
