using Microsoft.AspNetCore.Http;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubInstallationStateServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 5, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_generates_cryptographically_sized_state_and_stores_only_hash()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);

        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/supervisor/projects/11111111-1111-1111-1111-111111111111",
            TestContext.Current.CancellationToken);

        Assert.Equal(43, created.Value.Length);
        Assert.Equal(Now.AddMinutes(10).UtcDateTime, created.ExpiresAt);
        Assert.NotNull(store.State);
        Assert.NotEqual(created.Value, store.State.StateHash);
        Assert.Equal(64, store.State.StateHash.Length);
        Assert.Equal(ProjectId, store.State.ProjectId);
        Assert.Equal(UserId, store.State.InitiatingUserId);
    }

    [Fact]
    public async Task Create_generates_distinct_state_values_for_independent_flows()
    {
        var firstStore = new StubStore();
        var secondStore = new StubStore();
        var first = await CreateService(firstStore, Now).CreateAsync(
            ProjectId, UserId, GitHubInstallationFlowTypes.Direct, "/projects/one", TestContext.Current.CancellationToken);
        var second = await CreateService(secondStore, Now).CreateAsync(
            ProjectId, UserId, GitHubInstallationFlowTypes.Direct, "/projects/two", TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(firstStore.State?.StateHash, secondStore.State?.StateHash);
    }

    [Fact]
    public async Task Valid_state_is_consumed_once_and_returns_server_side_context()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/supervisor/projects/return-here",
            TestContext.Current.CancellationToken);

        var consumed = await service.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken);

        Assert.Equal(ProjectId, consumed.ProjectId);
        Assert.Equal(UserId, consumed.InitiatingUserId);
        Assert.Equal("/supervisor/projects/return-here", consumed.ReturnPath);
        Assert.Equal(Now.UtcDateTime, consumed.ConsumedAt);
    }

    [Fact]
    public async Task Expired_state_is_rejected()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/projects/return",
            TestContext.Current.CancellationToken);

        var later = CreateService(store, Now.AddMinutes(11));
        var exception = await Assert.ThrowsAsync<ApiException>(() => later.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("expired", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unknown_state_is_rejected()
    {
        var service = CreateService(new StubStore(), Now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ConsumeAsync(
            "unknown-state-value",
            UserId,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
        Assert.Contains("unknown", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replayed_state_is_rejected()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/projects/return",
            TestContext.Current.CancellationToken);

        await service.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken));

        Assert.Contains("already", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requested_state_is_one_time_and_replay_is_rejected()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var requestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var created = await service.CreateRequestedAsync(
            ProjectId,
            UserId,
            requestId,
            "/github/access-updated",
            TestContext.Current.CancellationToken);

        var consumed = await service.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Requested,
            TestContext.Current.CancellationToken);

        Assert.Equal(requestId, consumed.RepositoryAccessRequestId);
        Assert.Equal(GitHubInstallationFlowTypes.Requested, consumed.FlowType);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ConsumeAsync(
            created.Value,
            UserId,
            GitHubInstallationFlowTypes.Requested,
            TestContext.Current.CancellationToken));

        Assert.Contains("already", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(RequestedInstallationStateCreateOutcome.Expired, StatusCodes.Status410Gone)]
    [InlineData(RequestedInstallationStateCreateOutcome.NotPending, StatusCodes.Status409Conflict)]
    [InlineData(RequestedInstallationStateCreateOutcome.ContextMismatch, StatusCodes.Status404NotFound)]
    public async Task Requested_state_creation_honors_atomic_request_lifecycle_outcome(
        RequestedInstallationStateCreateOutcome outcome,
        int expectedStatusCode)
    {
        var store = new StubStore { RequestedCreateOutcome = outcome };
        var service = CreateService(store, Now);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.CreateRequestedAsync(
            ProjectId,
            UserId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "/github/access-updated",
            TestContext.Current.CancellationToken));

        Assert.Equal(expectedStatusCode, exception.StatusCode);
        Assert.Null(store.State);
    }

    [Fact]
    public async Task State_cannot_be_consumed_by_different_user()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/projects/return",
            TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ConsumeAsync(
            created.Value,
            Guid.NewGuid(),
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken));

        Assert.Contains("context", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(store.State?.ConsumedAt);
    }


    [Fact]
    public async Task Bind_installation_is_idempotent_for_same_installation_and_rejects_mismatch()
    {
        var store = new StubStore();
        var service = CreateService(store, Now);
        var created = await service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            "/projects/return",
            TestContext.Current.CancellationToken);

        var first = await service.BindInstallationAsync(
            created.Value,
            991,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken);
        var second = await service.BindInstallationAsync(
            created.Value,
            991,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken);

        Assert.Equal(991, first.PendingInstallationId);
        Assert.Equal(991, second.PendingInstallationId);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.BindInstallationAsync(
            created.Value,
            992,
            GitHubInstallationFlowTypes.Direct,
            TestContext.Current.CancellationToken));
        Assert.Equal(StatusCodes.Status400BadRequest, exception.StatusCode);
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("")]
    public async Task Create_rejects_browser_controlled_external_return_urls(string returnPath)
    {
        var service = CreateService(new StubStore(), Now);

        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(
            ProjectId,
            UserId,
            GitHubInstallationFlowTypes.Direct,
            returnPath,
            TestContext.Current.CancellationToken));
    }

    private static GitHubInstallationStateService CreateService(StubStore store, DateTimeOffset now) => new(
        store,
        new GitHubAppOptions(
            12345,
            "researchtrack-test",
            "Iv1.test-client",
            "test-client-secret",
            "/tmp/test.pem",
            new Uri("https://api.example.test/api/github/access-source/install/callback"),
            new Uri("https://app.example.test/"),
            TimeSpan.FromMinutes(10)),
        new FixedTimeProvider(now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubStore : IGitHubInstallationStateStore
    {
        public GitHubInstallationFlowState? State { get; private set; }

        public RequestedInstallationStateCreateOutcome RequestedCreateOutcome { get; set; } = RequestedInstallationStateCreateOutcome.Created;

        public Task CreateAsync(GitHubInstallationFlowState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }

        public Task<RequestedInstallationStateCreateOutcome> CreateRequestedAsync(
            GitHubInstallationFlowState state,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (RequestedCreateOutcome == RequestedInstallationStateCreateOutcome.Created)
            {
                State = state;
            }

            return Task.FromResult(RequestedCreateOutcome);
        }

        public Task<GitHubInstallationFlowState?> TryBindInstallationAsync(
            string stateHash,
            long installationId,
            string expectedFlowType,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (State is null
                || State.StateHash != stateHash
                || State.FlowType != expectedFlowType
                || State.ConsumedAt is not null
                || State.ExpiresAt <= now
                || (State.PendingInstallationId is long existing && existing != installationId))
            {
                return Task.FromResult<GitHubInstallationFlowState?>(null);
            }

            State.PendingInstallationId ??= installationId;
            State.AuthorizationStartedAt ??= now;
            return Task.FromResult<GitHubInstallationFlowState?>(State);
        }

        public Task<GitHubInstallationFlowState?> TryBindRequestedInstallationAsync(
            string stateHash,
            Guid repositoryAccessRequestId,
            long installationId,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (State is null || State.RepositoryAccessRequestId != repositoryAccessRequestId)
            {
                return Task.FromResult<GitHubInstallationFlowState?>(null);
            }
            State.PendingInstallationId = installationId;
            State.AuthorizationStartedAt ??= now;
            return Task.FromResult<GitHubInstallationFlowState?>(State);
        }

        public Task<ConsumedGitHubInstallationState?> TryConsumeAsync(
            string stateHash,
            Guid expectedUserId,
            string expectedFlowType,
            DateTime now,
            CancellationToken cancellationToken)
        {
            if (State is null
                || State.StateHash != stateHash
                || State.InitiatingUserId != expectedUserId
                || State.FlowType != expectedFlowType
                || State.ConsumedAt is not null
                || State.ExpiresAt <= now)
            {
                return Task.FromResult<ConsumedGitHubInstallationState?>(null);
            }

            State.ConsumedAt = now;
            return Task.FromResult<ConsumedGitHubInstallationState?>(new(
                State.ProjectId,
                State.InitiatingUserId,
                State.FlowType,
                State.ReturnPath,
                State.RepositoryAccessRequestId,
                State.PendingInstallationId,
                now));
        }

        public Task<GitHubInstallationFlowState?> FindAsync(
            string stateHash,
            CancellationToken cancellationToken) =>
            Task.FromResult(State?.StateHash == stateHash ? State : null);
    }
}
