using System.Text.Json;
using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;

namespace ResearchTrack.JiraService.MutationTests.Features;

public sealed class JiraIssueMapperTests
{
    [Fact]
    public void Map_MapsCompleteJiraIssueFields()
    {
        using var document = JsonDocument.Parse("""
        {
          "summary":"Implement Jira QA",
          "description":{"type":"doc","version":1},
          "issuetype":{"id":"10001","name":"Task","subtask":false},
          "status":{"id":"3","name":"In Progress","statusCategory":{"id":"4","key":"indeterminate","name":"In Progress"}},
          "priority":{"id":"2","name":"High"},
          "assignee":{"accountId":"acct-a","displayName":"Alice"},
          "reporter":{"accountId":"acct-r","displayName":"Reporter"},
          "parent":{"id":"900","key":"RT-1"},
          "resolution":{"id":"1","name":"Done"},
          "duedate":"2026-09-30T00:00:00+00:00",
          "resolutiondate":"2026-09-27T03:00:00+00:00",
          "created":"2026-09-20T01:00:00+00:00",
          "updated":"2026-09-27T02:00:00+00:00",
          "timetracking":{"originalEstimateSeconds":7200,"remainingEstimateSeconds":3600,"timeSpentSeconds":1800},
          "customfield_10016":5.5
        }
        """);
        var issue = new JiraIssue();
        var syncedAt = DateTimeOffset.UtcNow;

        JiraIssueMapper.Map(issue, "RT-42", document.RootElement, "customfield_10016", syncedAt);

        Assert.Equal("RT-42", issue.IssueKey);
        Assert.Equal("Implement Jira QA", issue.Summary);
        Assert.Contains("\"type\":\"doc\"", issue.DescriptionJson);
        Assert.Equal("Task", issue.IssueTypeName);
        Assert.False(issue.IsSubtask);
        Assert.Equal("In Progress", issue.StatusName);
        Assert.Equal("indeterminate", issue.StatusCategoryKey);
        Assert.Equal("High", issue.PriorityName);
        Assert.Equal("acct-a", issue.AssigneeAccountId);
        Assert.Equal("Alice", issue.AssigneeDisplayName);
        Assert.Equal("acct-r", issue.ReporterAccountId);
        Assert.Equal("RT-1", issue.ParentIssueKey);
        Assert.Equal("Done", issue.ResolutionName);
        Assert.Equal(7200, issue.OriginalEstimateSeconds);
        Assert.Equal(3600, issue.RemainingEstimateSeconds);
        Assert.Equal(1800, issue.TimeSpentSeconds);
        Assert.Equal(5.5m, issue.StoryPoints);
        Assert.Equal(syncedAt, issue.SyncedAt);
    }

    [Fact]
    public void Map_NullOptionalObjects_ClearPreviousValuesAndMissingStoryPoints()
    {
        using var document = JsonDocument.Parse("""
        {
          "summary":"Minimal",
          "description":null,
          "issuetype":{"name":"Task"},
          "status":{"name":"To Do","statusCategory":{"key":"new"}},
          "priority":null,
          "assignee":null,
          "reporter":null,
          "parent":null,
          "resolution":null
        }
        """);
        var issue = new JiraIssue
        {
            PriorityId = "old", PriorityName = "Old", AssigneeAccountId = "old", AssigneeDisplayName = "Old",
            ReporterAccountId = "old", ReporterDisplayName = "Old", ParentIssueId = "old", ParentIssueKey = "OLD-1",
            ResolutionId = "old", ResolutionName = "Old", StoryPoints = 13
        };

        JiraIssueMapper.Map(issue, "RT-2", document.RootElement, "customfield_10016", DateTimeOffset.UtcNow);

        Assert.Null(issue.DescriptionJson);
        Assert.Null(issue.PriorityId);
        Assert.Null(issue.PriorityName);
        Assert.Null(issue.AssigneeAccountId);
        Assert.Null(issue.AssigneeDisplayName);
        Assert.Null(issue.ReporterAccountId);
        Assert.Null(issue.ReporterDisplayName);
        Assert.Null(issue.ParentIssueId);
        Assert.Null(issue.ParentIssueKey);
        Assert.Null(issue.ResolutionId);
        Assert.Null(issue.ResolutionName);
        Assert.Null(issue.StoryPoints);
    }

    [Fact]
    public void Map_MissingSummaryAndNames_UsesSafeDefaults()
    {
        using var document = JsonDocument.Parse("""{"issuetype":{},"status":{}}""");
        var issue = new JiraIssue();

        JiraIssueMapper.Map(issue, "RT-3", document.RootElement, null, DateTimeOffset.UtcNow);

        Assert.Equal("(No summary)", issue.Summary);
        Assert.Equal("Unknown", issue.IssueTypeName);
        Assert.Equal("Unknown", issue.StatusName);
    }
}
