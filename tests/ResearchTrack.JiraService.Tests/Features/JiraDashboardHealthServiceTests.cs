using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Features;

public sealed class JiraDashboardHealthServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsConnectedHealthAndNotConnectedProjects()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var atRiskProjectId = Guid.NewGuid();
        var notConnectedProjectId = Guid.NewGuid();

        await SeedConnectionAsync(factory, atRiskProjectId, overdueIssues: 1);

        var service = new JiraDashboardHealthService(
            factory,
            auth,
            TimeProvider.System);

        var result = await service.GetAsync(
            [atRiskProjectId, notConnectedProjectId],
            TestContext.Current.CancellationToken);

        Assert.Equal(1, result.ConnectedCount);
        Assert.Equal(1, result.AtRiskCount);
        Assert.Equal(0, result.BehindCount);
        Assert.Equal(2, result.Projects.Count);
        Assert.Equal(
            "AT_RISK",
            result.Projects.Single(x => x.ProjectId == atRiskProjectId).Indicator);
        Assert.Equal(
            "NOT_CONNECTED",
            result.Projects.Single(x => x.ProjectId == notConnectedProjectId).Indicator);
        Assert.Equal(
            [atRiskProjectId, notConnectedProjectId],
            auth.ManageChecks);
    }

    [Fact]
    public async Task GetAsync_ClassifiesSevereJiraPressureAsBehind()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();

        await SeedConnectionAsync(factory, projectId, overdueIssues: 6);

        var result = await new JiraDashboardHealthService(
                factory,
                auth,
                TimeProvider.System)
            .GetAsync([projectId], TestContext.Current.CancellationToken);

        Assert.Equal(1, result.BehindCount);
        Assert.Equal("BEHIND", Assert.Single(result.Projects).Indicator);
    }

    private static async Task SeedConnectionAsync(
        TestJiraDbContextFactory factory,
        Guid projectId,
        int overdueIssues)
    {
        await using var db = factory.CreateDbContext();
        var connection = new JiraConnection
        {
            Id = Guid.NewGuid(),
            ResearchProjectId = projectId,
            CloudId = "cloud",
            WorkspaceName = "Workspace",
            JiraProjectId = "10000",
            JiraProjectKey = "RT",
            JiraProjectName = "ResearchTrack",
            AccessTokenProtected = "protected:access",
            ConnectedByUserId = Guid.NewGuid(),
            ConnectedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            SyncStatus = "SYNCED",
            LastSyncedAt = DateTimeOffset.UtcNow
        };
        db.JiraConnections.Add(connection);

        for (var index = 0; index < overdueIssues; index++)
        {
            db.JiraIssues.Add(new JiraIssue
            {
                Id = Guid.NewGuid(),
                JiraConnectionId = connection.Id,
                ResearchProjectId = projectId,
                JiraIssueId = $"{index + 1}",
                IssueKey = $"RT-{index + 1}",
                Summary = "Overdue",
                IssueTypeName = "Task",
                StatusName = "To Do",
                StatusCategoryKey = "new",
                DueDate = DateTimeOffset.UtcNow.AddDays(-1),
                SyncedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
