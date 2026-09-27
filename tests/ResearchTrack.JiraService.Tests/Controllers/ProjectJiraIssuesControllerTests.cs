using Microsoft.AspNetCore.Authorization;
using ResearchTrack.BuildingBlocks.Api.Security;
using ResearchTrack.JiraService.Controllers;
using ResearchTrack.JiraService.Contracts;
using ResearchTrack.JiraService.Features;
using ResearchTrack.JiraService.Tests.TestSupport;

namespace ResearchTrack.JiraService.Tests.Controllers;

public sealed class ProjectJiraIssuesControllerTests
{
    [Fact]
    public void Controller_IsProtectedByAuthorizeAttribute()
    {
        var attribute = Assert.Single(typeof(ProjectJiraIssuesController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>());

        Assert.Null(attribute.Policy);
    }

    [Fact]
    public void Refresh_RequiresSupervisorPolicy()
    {
        var method = typeof(ProjectJiraIssuesController).GetMethod(nameof(ProjectJiraIssuesController.Refresh))!;
        var attribute = Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(AuthSecurityConstants.Policies.SupervisorOnly, attribute.Policy);
    }

    [Fact]
    public async Task Refresh_PerformsProjectManageAuthorizationBeforeSynchronization()
    {
        var projectId = Guid.NewGuid();
        var auth = new RecordingProjectAuthorizationClient();
        var sync = new RecordingSyncService { Failure = new InvalidOperationException("stop-after-sync") };
        var controller = new ProjectJiraIssuesController(
            new StubIssueQueryService(), sync, new StubSprintService(), new StubWorkloadService(), new StubSyncStateService(), auth);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Refresh(projectId, TestContext.Current.CancellationToken));

        Assert.Equal("stop-after-sync", error.Message);
        Assert.Equal(projectId, Assert.Single(auth.ManageChecks));
        Assert.Equal(projectId, Assert.Single(sync.Projects));
    }

    [Fact]
    public async Task Refresh_WhenProjectManageAuthorizationFails_DoesNotSynchronize()
    {
        var auth = new RecordingProjectAuthorizationClient { ManageFailure = new InvalidOperationException("denied") };
        var sync = new RecordingSyncService();
        var controller = new ProjectJiraIssuesController(
            new StubIssueQueryService(), sync, new StubSprintService(), new StubWorkloadService(), new StubSyncStateService(), auth);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => controller.Refresh(Guid.NewGuid(), TestContext.Current.CancellationToken));

        Assert.Equal("denied", error.Message);
        Assert.Empty(sync.Projects);
    }

    private sealed class RecordingSyncService : IJiraSyncService
    {
        public List<Guid> Projects { get; } = [];
        public Exception? Failure { get; init; }

        public Task<JiraSyncResponse> SynchronizeAsync(Guid projectId, CancellationToken ct, string trigger = "MANUAL")
        {
            Projects.Add(projectId);
            if (Failure is not null)
                return Task.FromException<JiraSyncResponse>(Failure);

            return Task.FromResult(new JiraSyncResponse(0, 0, DateTimeOffset.UtcNow));
        }
    }

    private sealed class StubIssueQueryService : IJiraIssueQueryService
    {
        public Task<JiraIssueListResponse> GetIssuesAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult(new JiraIssueListResponse([], new(0, 0, 0, 0), new("SYNCED", null, null)));
        public Task<JiraHealthResponse> GetHealthAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult(new JiraHealthResponse(0, 0, 0, 0, new(0, 0, 0), [], 0, null));
    }

    private sealed class StubSprintService : IJiraSprintProgressService
    {
        public Task<JiraSprintProgressResponse> GetAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult(new JiraSprintProgressResponse(false, null, [], new(0, 0, 0, 0), new("SYNCED", null, null)));
    }

    private sealed class StubWorkloadService : IJiraWorkloadService
    {
        public Task<JiraWorkloadResponse> GetAsync(Guid projectId, CancellationToken ct) =>
            Task.FromResult(new JiraWorkloadResponse([], new(0, 0, 0), new(0, 0, 0, 0, 0), new("SYNCED", null, null)));
    }

    private sealed class StubSyncStateService : IJiraSyncStateQueryService
    {
        public Task<JiraProjectSyncStateResponse> GetAsync(Guid projectId, CancellationToken cancellationToken) =>
            Task.FromResult(new JiraProjectSyncStateResponse(false, 0, null, null, null, null, null, null));
    }
}
