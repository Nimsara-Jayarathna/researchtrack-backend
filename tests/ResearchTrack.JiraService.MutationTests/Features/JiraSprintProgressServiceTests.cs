using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.MutationTests.TestSupport;

namespace ResearchTrack.JiraService.MutationTests.Features;

public sealed class JiraSprintProgressServiceTests
{
    [Fact]
    public async Task GetAsync_ActiveSprintWithMixedStatuses_ReturnsCorrectProgress()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId, [
            Issue("RT-1", "new", 3),
            Issue("RT-2", "indeterminate", 5),
            Issue("RT-3", "done", 8)
        ]);

        var result = await new JiraSprintProgressService(factory, auth).GetAsync(projectId, CancellationToken.None);

        Assert.True(result.HasActiveSprint);
        Assert.NotNull(result.ActiveSprint);
        Assert.Equal(3, result.ActiveSprint!.IssuesTotal);
        Assert.Equal(1, result.ActiveSprint.StatusBreakdown.ToDo);
        Assert.Equal(1, result.ActiveSprint.StatusBreakdown.InProgress);
        Assert.Equal(1, result.ActiveSprint.StatusBreakdown.Done);
        Assert.Equal(1, result.ActiveSprint.IssuesDone);
        Assert.Equal(2, result.ActiveSprint.IssuesRemaining);
        Assert.Equal(33, result.ActiveSprint.CompletionPercent);
        Assert.True(result.ActiveSprint.SprintPointsAvailable);
        Assert.Equal(16m, result.ActiveSprint.SprintPointsTotal);
        Assert.Equal(8m, result.ActiveSprint.SprintPointsDone);
        Assert.Equal(projectId, Assert.Single(auth.AccessChecks));
    }

    [Fact]
    public async Task GetAsync_ZeroIssueSprint_DoesNotDivideByZero()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await SeedAsync(factory, projectId, []);

        var result = await new JiraSprintProgressService(factory, auth).GetAsync(projectId, CancellationToken.None);

        Assert.Equal(0, result.ActiveSprint!.IssuesTotal);
        Assert.Equal(0, result.ActiveSprint.CompletionPercent);
        Assert.False(result.ActiveSprint.SprintPointsAvailable);
    }

    [Fact]
    public async Task GetAsync_NoActiveSprint_ReturnsHasActiveSprintFalseButKeepsSprintSummary()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        await using (var db = factory.CreateDbContext())
        {
            db.JiraConnections.Add(Connection(projectId));
            db.JiraSprints.Add(new JiraSprint
            {
                Id = Guid.NewGuid(), JiraConnectionId = db.JiraConnections.Local.Single().Id,
                ResearchProjectId = projectId, JiraSprintId = 10, JiraBoardId = 1,
                Name = "Closed Sprint", State = "closed", SyncedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync(CancellationToken.None);
        }

        var result = await new JiraSprintProgressService(factory, auth).GetAsync(projectId, CancellationToken.None);

        Assert.False(result.HasActiveSprint);
        Assert.Null(result.ActiveSprint);
        Assert.Equal(1, result.Summary.Total);
        Assert.Equal(1, result.Summary.Closed);
    }

    [Fact]
    public async Task GetAsync_WithoutConnection_ThrowsNotFoundAfterAuthorizationCheck()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();

        var error = await Assert.ThrowsAsync<ResearchTrack.BuildingBlocks.Api.Exceptions.ApiException>(
            () => new JiraSprintProgressService(factory, auth).GetAsync(projectId, CancellationToken.None));

        Assert.Equal(404, error.StatusCode);
        Assert.Equal(projectId, Assert.Single(auth.AccessChecks));
    }

    private static async Task SeedAsync(TestJiraDbContextFactory factory, Guid projectId, IReadOnlyList<(string key, string category, decimal? points)> issues)
    {
        await using var db = factory.CreateDbContext();
        var connection = Connection(projectId);
        var sprint = new JiraSprint
        {
            Id = Guid.NewGuid(), JiraConnectionId = connection.Id, ResearchProjectId = projectId,
            JiraSprintId = 101, JiraBoardId = 1, Name = "Sprint 1", State = "active",
            StartDate = DateTimeOffset.UtcNow.AddDays(-2), EndDate = DateTimeOffset.UtcNow.AddDays(12), SyncedAt = DateTimeOffset.UtcNow
        };
        db.JiraConnections.Add(connection);
        db.JiraSprints.Add(sprint);
        foreach (var item in issues)
        {
            var issue = new JiraIssue
            {
                Id = Guid.NewGuid(), JiraConnectionId = connection.Id, ResearchProjectId = projectId,
                JiraIssueId = item.key, IssueKey = item.key, Summary = item.key, IssueTypeName = "Task",
                StatusName = item.category, StatusCategoryKey = item.category, StoryPoints = item.points, SyncedAt = DateTimeOffset.UtcNow
            };
            db.JiraIssues.Add(issue);
            db.JiraIssueSprints.Add(new JiraIssueSprint { JiraIssueId = issue.Id, JiraSprintId = sprint.Id });
        }
        await db.SaveChangesAsync(CancellationToken.None);
    }

    private static JiraConnection Connection(Guid projectId) => new()
    {
        Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "cloud", WorkspaceName = "Workspace",
        JiraProjectId = "10000", JiraProjectKey = "RT", JiraProjectName = "ResearchTrack",
        AccessTokenProtected = "protected:access", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow, SyncStatus = "SYNCED"
    };

    private static (string key, string category, decimal? points) Issue(string key, string category, decimal? points) => (key, category, points);
}
