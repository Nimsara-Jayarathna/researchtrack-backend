using ResearchTrack.JiraService.Domain;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Features;

public sealed class JiraSyncStateQueryServiceTests
{
    [Fact]
    public async Task GetAsync_DisconnectedProject_ReturnsConnectedFalse()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        await using var db = factory.CreateDbContext();
        var projectId = Guid.NewGuid();

        var result = await new JiraSyncStateQueryService(db, auth).GetAsync(projectId, TestContext.Current.CancellationToken);

        Assert.False(result.Connected);
        Assert.Equal(0, result.SyncRevision);
        Assert.Equal(projectId, Assert.Single(auth.AccessChecks));
    }

    [Fact]
    public async Task GetAsync_ConnectedProject_ReturnsPersistedSyncAndWebhookState()
    {
        var factory = new TestJiraDbContextFactory();
        var auth = new RecordingProjectAuthorizationClient();
        var projectId = Guid.NewGuid();
        var syncedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using var db = factory.CreateDbContext();
        db.JiraConnections.Add(new JiraConnection
        {
            Id = Guid.NewGuid(), ResearchProjectId = projectId, CloudId = "cloud", WorkspaceName = "Workspace",
            JiraProjectId = "100", JiraProjectKey = "RT", JiraProjectName = "ResearchTrack",
            AccessTokenProtected = "protected", ConnectedByUserId = Guid.NewGuid(), ConnectedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow, SyncRevision = 7, SyncStatus = "SYNCED", LastSyncedAt = syncedAt,
            WebhookStatus = "ACTIVE", LastWebhookAt = syncedAt.AddMinutes(1), LastReconciledAt = syncedAt.AddMinutes(2)
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var result = await new JiraSyncStateQueryService(db, auth).GetAsync(projectId, TestContext.Current.CancellationToken);

        Assert.True(result.Connected);
        Assert.Equal(7, result.SyncRevision);
        Assert.Equal("SYNCED", result.SyncStatus);
        Assert.Equal("ACTIVE", result.WebhookStatus);
    }
}
