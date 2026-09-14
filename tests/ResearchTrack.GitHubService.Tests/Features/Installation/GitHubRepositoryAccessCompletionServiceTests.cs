using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Features.Synchronization;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubRepositoryAccessCompletionServiceTests
{
    private static readonly Guid RequestId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RepositoryId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid LinkId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 8, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Successful_new_completion_hands_off_initial_sync_exactly_once()
    {
        var store = new StubCompletionStore(NewCompletion());
        var sync = new StubSyncRequester();
        var linkStore = new StubRepositoryLinkStore();
        var service = CreateService(store, sync, linkStore);

        var result = await service.CompleteAsync(
            RequestId,
            Installation(),
            Repository(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.AlreadyCompleted);
        Assert.True(result.InitialSyncHandoffSucceeded);
        Assert.Equal(1, sync.CallCount);
        Assert.Equal(LinkId, sync.LastRequest?.LinkedRepositoryId);
        Assert.Equal(0, linkStore.MarkFailedCallCount);
    }

    [Fact]
    public async Task Duplicate_completed_request_does_not_handoff_initial_sync_again()
    {
        var store = new StubCompletionStore(AlreadyCompleted());
        var sync = new StubSyncRequester();
        var service = CreateService(store, sync, new StubRepositoryLinkStore());

        var result = await service.CompleteAsync(
            RequestId,
            Installation(),
            Repository(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(result.AlreadyCompleted);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task Concurrent_logical_completion_results_produce_one_initial_sync_handoff()
    {
        var store = new ConcurrentCompletionStore();
        var sync = new StubSyncRequester();
        var service = CreateService(store, sync, new StubRepositoryLinkStore());

        var results = await Task.WhenAll(
            service.CompleteAsync(RequestId, Installation(), Repository(), TestContext.Current.CancellationToken),
            service.CompleteAsync(RequestId, Installation(), Repository(), TestContext.Current.CancellationToken));

        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.Single(results, result => !result.AlreadyCompleted);
        Assert.Single(results, result => result.AlreadyCompleted);
        Assert.Equal(1, sync.CallCount);
    }

    [Fact]
    public async Task Failed_persistence_never_requests_initial_sync()
    {
        var failed = NewCompletion() with
        {
            Succeeded = false,
            ErrorCode = "repository_already_linked",
            InitialSyncRequest = null,
            SourceId = null,
            RepositoryId = null,
            LinkedRepositoryId = null
        };
        var sync = new StubSyncRequester();
        var service = CreateService(
            new StubCompletionStore(failed),
            sync,
            new StubRepositoryLinkStore());

        var result = await service.CompleteAsync(
            RequestId,
            Installation(),
            Repository(),
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("repository_already_linked", result.ErrorCode);
        Assert.Equal(0, sync.CallCount);
    }

    [Fact]
    public async Task Queue_handoff_failure_keeps_completion_successful_and_marks_link_for_retry_visibility()
    {
        var sync = new StubSyncRequester { Failure = new InvalidOperationException("queue unavailable") };
        var linkStore = new StubRepositoryLinkStore();
        var service = CreateService(new StubCompletionStore(NewCompletion()), sync, linkStore);

        var result = await service.CompleteAsync(
            RequestId,
            Installation(),
            Repository(),
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.False(result.InitialSyncHandoffSucceeded);
        Assert.Equal(1, sync.CallCount);
        Assert.Equal(1, linkStore.MarkFailedCallCount);
        Assert.Equal(LinkId, linkStore.LastMarkedLinkId);
    }

    private static GitHubRepositoryAccessCompletionService CreateService(
        IGitHubRepositoryAccessCompletionStore store,
        IInitialRepositorySyncRequester sync,
        IRepositoryLinkStore linkStore) => new(
        store,
        sync,
        linkStore,
        new FixedTimeProvider(Now),
        NullLogger<GitHubRepositoryAccessCompletionService>.Instance);

    private static OwnerGrantedRepositoryCompletionPersistenceResult NewCompletion() => new(
        RequestId,
        ProjectId,
        true,
        false,
        null,
        SourceId,
        RepositoryId,
        LinkId,
        987654,
        new InitialRepositorySyncRequest(ProjectId, LinkId, SourceId, RepositoryId, 987654));

    private static OwnerGrantedRepositoryCompletionPersistenceResult AlreadyCompleted() =>
        NewCompletion() with { AlreadyCompleted = true, InitialSyncRequest = null };

    private static GitHubInstallationInfo Installation() => new(777, "openai", "ORG");

    private static GitHubInstallationRepository Repository() => new(
        987654,
        "openai",
        "researchtrack",
        "openai/researchtrack",
        "https://github.com/openai/researchtrack",
        "main",
        true);

    private sealed class StubCompletionStore(OwnerGrantedRepositoryCompletionPersistenceResult result)
        : IGitHubRepositoryAccessCompletionStore
    {
        public Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync(
            Guid requestId,
            GitHubInstallationInfo installation,
            GitHubInstallationRepository repository,
            DateTime now,
            CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ConcurrentCompletionStore : IGitHubRepositoryAccessCompletionStore
    {
        private int _calls;

        public Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync(
            Guid requestId,
            GitHubInstallationInfo installation,
            GitHubInstallationRepository repository,
            DateTime now,
            CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            return Task.FromResult(call == 1 ? NewCompletion() : AlreadyCompleted());
        }
    }

    private sealed class StubSyncRequester : IInitialRepositorySyncRequester
    {
        private int _callCount;
        public Exception? Failure { get; init; }
        public int CallCount => _callCount;
        public InitialRepositorySyncRequest? LastRequest { get; private set; }

        public Task RequestAsync(InitialRepositorySyncRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastRequest = request;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubRepositoryLinkStore : IRepositoryLinkStore
    {
        public int MarkFailedCallCount { get; private set; }
        public Guid? LastMarkedLinkId { get; private set; }

        public Task<RepositoryLinkPersistenceResult> CreateLinksAsync(
            Guid projectId,
            Guid sourceId,
            Guid userId,
            IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
            DateTime now,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MarkSyncFailedAsync(
            Guid linkedRepositoryId,
            DateTime now,
            CancellationToken cancellationToken)
        {
            MarkFailedCallCount++;
            LastMarkedLinkId = linkedRepositoryId;
            return Task.CompletedTask;
        }

        public Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
