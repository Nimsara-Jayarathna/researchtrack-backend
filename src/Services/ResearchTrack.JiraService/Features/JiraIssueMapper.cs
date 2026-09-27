using System.Text.Json;
using ResearchTrack.JiraService.Domain;

namespace ResearchTrack.JiraService.Features;

internal static class JiraIssueMapper
{
    public static void Map(JiraIssue issue, string issueKey, JsonElement fields, string? storyPointsFieldId, DateTimeOffset syncedAt)
    {
        issue.IssueKey = issueKey;
        issue.Summary = String(fields, "summary") ?? "(No summary)";
        issue.DescriptionJson = fields.TryGetProperty("description", out var description) &&
                                description.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? description.GetRawText()
            : null;

        if (fields.TryGetProperty("issuetype", out var issueType))
        {
            issue.IssueTypeId = String(issueType, "id");
            issue.IssueTypeName = String(issueType, "name") ?? "Unknown";
            issue.IsSubtask = Bool(issueType, "subtask");
        }

        if (fields.TryGetProperty("status", out var status))
        {
            issue.StatusId = String(status, "id");
            issue.StatusName = String(status, "name") ?? "Unknown";
            if (status.TryGetProperty("statusCategory", out var statusCategory))
            {
                issue.StatusCategoryId = String(statusCategory, "id");
                issue.StatusCategoryKey = String(statusCategory, "key");
                issue.StatusCategoryName = String(statusCategory, "name");
            }
        }

        if (fields.TryGetProperty("priority", out var priority) && priority.ValueKind == JsonValueKind.Object)
        {
            issue.PriorityId = String(priority, "id");
            issue.PriorityName = String(priority, "name");
        }
        else
        {
            issue.PriorityId = null;
            issue.PriorityName = null;
        }

        Person(fields, "assignee", out var assigneeId, out var assigneeName);
        issue.AssigneeAccountId = assigneeId;
        issue.AssigneeDisplayName = assigneeName;
        Person(fields, "reporter", out var reporterId, out var reporterName);
        issue.ReporterAccountId = reporterId;
        issue.ReporterDisplayName = reporterName;

        if (fields.TryGetProperty("parent", out var parent) && parent.ValueKind == JsonValueKind.Object)
        {
            issue.ParentIssueId = String(parent, "id");
            issue.ParentIssueKey = String(parent, "key");
        }
        else
        {
            issue.ParentIssueId = null;
            issue.ParentIssueKey = null;
        }

        if (fields.TryGetProperty("resolution", out var resolution) && resolution.ValueKind == JsonValueKind.Object)
        {
            issue.ResolutionId = String(resolution, "id");
            issue.ResolutionName = String(resolution, "name");
        }
        else
        {
            issue.ResolutionId = null;
            issue.ResolutionName = null;
        }

        issue.DueDate = Date(fields, "duedate");
        issue.ResolutionDate = Date(fields, "resolutiondate");
        issue.JiraCreatedAt = Date(fields, "created");
        issue.JiraUpdatedAt = Date(fields, "updated");

        if (fields.TryGetProperty("timetracking", out var timeTracking) && timeTracking.ValueKind == JsonValueKind.Object)
        {
            issue.OriginalEstimateSeconds = Long(timeTracking, "originalEstimateSeconds");
            issue.RemainingEstimateSeconds = Long(timeTracking, "remainingEstimateSeconds");
            issue.TimeSpentSeconds = Long(timeTracking, "timeSpentSeconds");
        }

        issue.StoryPoints = !string.IsNullOrWhiteSpace(storyPointsFieldId) &&
                            fields.TryGetProperty(storyPointsFieldId, out var storyPoints) &&
                            storyPoints.ValueKind == JsonValueKind.Number &&
                            storyPoints.TryGetDecimal(out var value)
            ? value
            : null;
        issue.SyncedAt = syncedAt;
    }

    private static void Person(JsonElement fields, string name, out string? id, out string? displayName)
    {
        id = null;
        displayName = null;
        if (!fields.TryGetProperty(name, out var person) || person.ValueKind != JsonValueKind.Object) return;
        id = String(person, "accountId");
        displayName = String(person, "displayName");
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static long? Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.TryGetInt64(out var value) ? value : null;

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
        DateTimeOffset.TryParse(property.GetString(), out var date)
            ? date
            : null;
}
