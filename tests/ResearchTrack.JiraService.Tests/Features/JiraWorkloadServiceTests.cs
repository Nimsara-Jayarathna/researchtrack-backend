using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Features;

public sealed class JiraWorkloadServiceTests
{
    [Fact]
    public async Task GetAsync_MultipleAssigneesAndUnassigned_ReturnsCorrectSummary()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId,
            Issue("RT-1", "new", "a", "Alice", 3),
            Issue("RT-2", "indeterminate", "a", "Alice", 5),
            Issue("RT-3", "done", "b", "Bob", 8),
            Issue("RT-4", "new", null, null, 2));

        var result = await new JiraWorkloadService(factory, auth).GetAsync(projectId, TestContext.Current.CancellationToken);

        Assert.Equal(4, result.Summary.TotalIssues);
        Assert.Equal(3, result.Summary.ActiveIssues);
        Assert.Equal(1, result.Summary.DoneIssues);
        Assert.Equal(2, result.Summary.AssignedActiveIssues);
        Assert.Equal(1, result.Summary.UnassignedActiveIssues);
        Assert.Equal(1, result.Unassigned.Total);
        var alice = Assert.Single(result.Members, x => x.DisplayName == "Alice");
        Assert.Equal(2, alice.Active);
        Assert.Equal(1, alice.ToDo);
        Assert.Equal(1, alice.InProgress);
        Assert.Equal(8m, alice.ActiveStoryPoints);
        var bob = Assert.Single(result.Members, x => x.DisplayName == "Bob");
        Assert.Equal(0, bob.Active);
        Assert.Equal(1, bob.Done);
        Assert.Equal(projectId, Assert.Single(auth.AccessChecks));
    }

    [Fact]
    public async Task GetAsync_DisplayNameOnlyAssigneesWithDifferentCase_AreGroupedTogether()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId,
            Issue("RT-1", "new", null, "Alice", null),
            Issue("RT-2", "done", null, " alice ", null));

        var result = await new JiraWorkloadService(factory, auth).GetAsync(projectId, TestContext.Current.CancellationToken);

        var member = Assert.Single(result.Members);
        Assert.Equal(2, member.Total);
        Assert.Equal(1, member.Active);
        Assert.Equal(1, member.Done);
    }

    [Fact]
    public async Task GetAsync_NoIssues_ReturnsZeroSummary()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId);

        var result = await new JiraWorkloadService(factory, auth).GetAsync(projectId, TestContext.Current.CancellationToken);

        Assert.Empty(result.Members);
        Assert.Equal(0, result.Summary.TotalIssues);
        Assert.Equal(0, result.Unassigned.Total);
    }

    [Fact]
    public async Task GetAsync_WithoutConnection_ThrowsNotFound()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();

        var error = await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(
            () => new JiraWorkloadService(factory, auth).GetAsync(Guid.NewGuid(), TestContext.Current.CancellationToken));

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
            UpdatedAt = DateTimeOffset.UtcNow, SyncStatus = "SYNCED"
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

    private static JiraIssue Issue(string key, string category, string? accountId, string? displayName, decimal? points) => new()
    {
        Id = Guid.NewGuid(), JiraIssueId = key, IssueKey = key, Summary = key, IssueTypeName = "Task",
        StatusName = category, StatusCategoryKey = category, AssigneeAccountId = accountId,
        AssigneeDisplayName = displayName, StoryPoints = points, SyncedAt = DateTimeOffset.UtcNow
    };
}
