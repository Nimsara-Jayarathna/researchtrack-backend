using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Features;

public sealed class JiraIssueQueryServiceTests
{
    [Fact]
    public async Task GetIssuesAsync_ReturnsSortedIssuesAndStatusSummary()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId,
            Issue("RT-2", "indeterminate"),
            Issue("RT-1", "new"),
            Issue("RT-3", "done"));

        var result = await new JiraIssueQueryService(factory, auth).GetIssuesAsync(projectId, TestContext.Current.CancellationToken);

        Assert.Equal(["RT-1", "RT-2", "RT-3"], result.Items.Select(x => x.IssueKey).ToArray());
        Assert.Equal(3, result.Summary.Total);
        Assert.Equal(1, result.Summary.ToDo);
        Assert.Equal(1, result.Summary.InProgress);
        Assert.Equal(1, result.Summary.Done);
        Assert.Equal(projectId, Assert.Single(auth.AccessChecks));
    }

    [Fact]
    public async Task GetHealthAsync_CalculatesCompletionOverdueHighPriorityAndBugRatio()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        var overdue = Issue("RT-1", "new");
        overdue.PriorityName = "High";
        overdue.DueDate = DateTimeOffset.UtcNow.AddDays(-1);
        overdue.IssueTypeName = "Bug";
        var done = Issue("RT-2", "done");
        done.PriorityName = "Highest";
        done.IssueTypeName = "Task";
        await SeedAsync(factory, projectId, overdue, done);

        var result = await new JiraIssueQueryService(factory, auth).GetHealthAsync(projectId, TestContext.Current.CancellationToken);

        Assert.Equal(50, result.CompletionPercent);
        Assert.Equal(1, result.OpenIssues);
        Assert.Equal(1, result.OverdueIssues);
        Assert.Equal(1, result.HighPriorityOpen);
        Assert.Equal(0.5d, result.BugRatio, 3);
    }

    [Fact]
    public async Task GetIssuesAsync_AccessFailureStopsBeforeReadingData()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient { AccessFailure = new InvalidOperationException("denied") };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new JiraIssueQueryService(factory, auth).GetIssuesAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal("denied", error.Message);
    }

    [Fact]
    public async Task GetIssuesAsync_WithoutConnection_ThrowsNotFound()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();

        var error = await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(
            () => new JiraIssueQueryService(factory, auth).GetIssuesAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal(404, error.StatusCode);
    }

    private static async Task SeedAsync(TestJiraDbContextFactory factory, Guid projectId, params JiraIssue[] issues)
    {
        await using var db = factory.CreateDbContext();
        var connection = new JiraConnection
        {
            Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "cloud", WorkspaceName = "Workspace",
            JiraProjectId = "10000", JiraProjectKey = "RT", JiraProjectName = "ResearchTrack",
            AccessTokenProtected = "protected:access", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow, SyncStatus = "SYNCED", LastSyncedAt = DateTimeOffset.UtcNow
        };
        db.JiraConnections.Add(connection);
        foreach (var issue in issues)
        {
            issue.JiraConnectionId = connection.Id;
            issue.ResearchProjectId = projectId;
            db.JiraIssues.Add(issue);
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static JiraIssue Issue(string key, string category) => new()
    {
        Id = Guid.NewGuid(), JiraIssueId = key, IssueKey = key, Summary = key, IssueTypeName = "Task",
        StatusName = category, StatusCategoryKey = category, SyncedAt = DateTimeOffset.UtcNow
    };
}
