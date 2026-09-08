using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features;

public sealed class RepositoryLinkServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RepositoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LinkId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task Successful_persistence_triggers_initial_sync_exactly_once_after_commit()
    {
        var order = new List<string>();
        var store = new StubStore(order);
        var sync = new StubSyncRequester(order);
        var service = CreateService(new StubAuthorizationClient(), store, sync);

        var response = await service.LinkAsync(
            UserId,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(["persist", "sync"], order);
        Assert.Equal(1, sync.CallCount);
        Assert.Equal(LinkId, sync.LastRequest?.LinkedRepositoryId);
        Assert.Equal("PENDING", Assert.Single(response.Repositories).SyncStatus);
    }

    [Fact]
    public async Task Authorization_failure_does_not_persist_or_trigger_sync()
    {
        var authorization = new StubAuthorizationClient
        {
            Failure = new ApiException(
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "Forbidden")
        };
        var store = new StubStore([]);
        var sync = new StubSyncRequester([]);
        var service = CreateService(authorization, store, sync);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.LinkAsync(
            UserId,
            ValidRequest(),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status403Forbidden, exception.StatusCode);
        Assert.Equal(0, store.CreateCallCount);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task Persistence_conflict_does_not_trigger_sync()
    {
        var store = new StubStore([])
        {
            CreateFailure = new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Duplicate")
        };
        var sync = new StubSyncRequester([]);
        var service = CreateService(new StubAuthorizationClient(), store, sync);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.LinkAsync(
            UserId,
            ValidRequest(),
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status409Conflict, exception.StatusCode);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task Failed_sync_handoff_keeps_link_and_exposes_failed_status()
    {
        var store = new StubStore([]);
        var sync = new StubSyncRequester([]) { Failure = new InvalidOperationException("Unavailable") };
        var service = CreateService(new StubAuthorizationClient(), store, sync);

        var response = await service.LinkAsync(
            UserId,
            ValidRequest(),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, sync.CallCount);
        Assert.Equal(1, store.MarkFailedCallCount);
        Assert.Equal("FAILED", Assert.Single(response.Repositories).SyncStatus);
    }

    [Fact]
    public async Task Invalid_request_does_not_authorize_persist_or_trigger_sync()
    {
        var authorization = new StubAuthorizationClient();
        var store = new StubStore([]);
        var sync = new StubSyncRequester([]);
        var service = CreateService(authorization, store, sync);
        var request = new LinkGitHubRepositoriesRequest(
            ProjectId,
            SourceId,
            [
                new LinkGitHubRepositoryRequestItem(RepositoryId, null, true),
                new LinkGitHubRepositoryRequestItem(RepositoryId, null, false)
            ]);

        await Assert.ThrowsAsync<ApiValidationException>(() => service.LinkAsync(
            UserId,
            request,
            TestContext.Current.CancellationToken));

        Assert.Equal(0, authorization.CallCount);
        Assert.Equal(0, store.CreateCallCount);
        Assert.Equal(0, sync.CallCount);
    }

    private static RepositoryLinkService CreateService(
        IProjectAuthorizationClient authorization,
        IRepositoryLinkStore store,
        IInitialRepositorySyncRequester sync) => new(
            authorization,
            store,
            sync,
            new FixedTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<RepositoryLinkService>.Instance);

    private static LinkGitHubRepositoriesRequest ValidRequest() => new(
        ProjectId,
        SourceId,
        [new LinkGitHubRepositoryRequestItem(RepositoryId, "Research repository", true)]);

    private static ProjectGitHubRepositoriesResponse Response(string status) => new(
        ProjectId,
        5,
        5,
        [],
        [new ProjectRepositoryLinkResponse(
            LinkId,
            SourceId,
            "PUBLIC_URL",
            RepositoryId,
            1296269,
            "openai/example",
            "example",
            "Research repository",
            "openai",
            "main",
            "https://github.com/openai/example",
            true,
            true,
            new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc),
            null,
            status)]);

    private sealed class StubAuthorizationClient : IProjectAuthorizationClient
    {
        public Exception? Failure { get; init; }
        public int CallCount { get; private set; }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            CallCount++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubStore(List<string> order) : IRepositoryLinkStore
    {
        public Exception? CreateFailure { get; init; }
        public int CreateCallCount { get; private set; }
        public int MarkFailedCallCount { get; private set; }

        public Task<RepositoryLinkPersistenceResult> CreateLinksAsync(
            Guid projectId,
            Guid sourceId,
            Guid userId,
            IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
            DateTime now,
            CancellationToken cancellationToken)
        {
            CreateCallCount++;
            order.Add("persist");
            if (CreateFailure is not null)
            {
                return Task.FromException<RepositoryLinkPersistenceResult>(CreateFailure);
            }
            return Task.FromResult(new RepositoryLinkPersistenceResult(
                Response("PENDING"),
                [new InitialRepositorySyncRequest(ProjectId, LinkId, SourceId, RepositoryId, 1296269)]));
        }

        public Task MarkSyncFailedAsync(
            Guid linkedRepositoryId,
            DateTime now,
            CancellationToken cancellationToken)
        {
            MarkFailedCallCount++;
            return Task.CompletedTask;
        }

        public Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken) => Task.FromResult(Response("FAILED"));
    }

    private sealed class StubSyncRequester(List<string> order) : IInitialRepositorySyncRequester
    {
        public Exception? Failure { get; init; }
        public int CallCount { get; private set; }
        public InitialRepositorySyncRequest? LastRequest { get; private set; }

        public Task RequestAsync(
            InitialRepositorySyncRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            order.Add("sync");
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
