using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubRepositoryAccessContinuationServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid RequestId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);
    private const string RawToken = "owner-grant-token";

    [Fact]
    public async Task Pending_request_can_create_only_request_bound_installation_state()
    {
        var request = PendingRequest();
        var store = new StubRequestStore { Request = request };
        var state = new StubStateService();
        var service = CreateService(store, state);

        var result = await service.ContinueAsync(RawToken, TestContext.Current.CancellationToken);

        Assert.True(state.CreateRequestedWasCalled);
        Assert.Equal(RequestId, state.RequestId);
        Assert.Equal(ProjectId, state.ProjectId);
        Assert.Equal(UserId, state.UserId);
        Assert.Equal("/github/access-updated", state.ReturnPath);
        Assert.Contains("github.com/apps/researchtrack-test/installations/new", result.GitHubAuthorizeUrl, StringComparison.Ordinal);
        Assert.DoesNotContain(RawToken, result.GitHubAuthorizeUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Completed, StatusCodes.Status409Conflict)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Failed, StatusCodes.Status409Conflict)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Expired, StatusCodes.Status410Gone)]
    public async Task Terminal_request_cannot_continue_or_create_state(string status, int expectedStatusCode)
    {
        var request = PendingRequest();
        request.Status = status;
        request.ConsumedAt = Now.AddSeconds(-1).UtcDateTime;
        var store = new StubRequestStore { Request = request };
        var state = new StubStateService();
        var service = CreateService(store, state);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ContinueAsync(
            RawToken,
            TestContext.Current.CancellationToken));

        Assert.Equal(expectedStatusCode, exception.StatusCode);
        Assert.False(state.CreateRequestedWasCalled);
    }

    [Fact]
    public async Task Request_expiring_before_continue_is_materialized_and_cannot_create_state()
    {
        var request = PendingRequest();
        request.ExpiresAt = Now.AddMilliseconds(-1).UtcDateTime;
        var store = new StubRequestStore { Request = request };
        var state = new StubStateService();
        var service = CreateService(store, state);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ContinueAsync(
            RawToken,
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status410Gone, exception.StatusCode);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Expired, store.Request!.Status);
        Assert.False(state.CreateRequestedWasCalled);
    }

    [Fact]
    public async Task Unknown_token_cannot_create_state()
    {
        var store = new StubRequestStore { Request = PendingRequest() };
        var state = new StubStateService();
        var service = CreateService(store, state);

        var exception = await Assert.ThrowsAsync<ApiException>(() => service.ContinueAsync(
            "unknown-token",
            TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status404NotFound, exception.StatusCode);
        Assert.False(state.CreateRequestedWasCalled);
    }

    private static GitHubRepositoryAccessContinuationService CreateService(
        StubRequestStore store,
        StubStateService state)
    {
        var timeProvider = new FixedTimeProvider(Now);
        var tokenService = new GitHubRepositoryAccessTokenService(
            store,
            timeProvider,
            NullLogger<GitHubRepositoryAccessTokenService>.Instance);
        return new GitHubRepositoryAccessContinuationService(
            tokenService,
            store,
            state,
            new GitHubAppOptions(
                12345,
                "researchtrack-test",
                "Iv1.test-client",
                "test-client-secret",
                "/tmp/test.pem",
                new Uri("https://api.example.test/api/github/access-source/install/callback"),
                new Uri("https://app.example.test/"),
                TimeSpan.FromMinutes(10)),
            timeProvider,
            NullLogger<GitHubRepositoryAccessContinuationService>.Instance);
    }

    private static GitHubRepositoryAccessRequest PendingRequest() => new()
    {
        Id = RequestId,
        ProjectId = ProjectId,
        InitiatingUserId = UserId,
        RequestedOwner = "openai",
        RequestedRepositoryName = "researchtrack",
        RequestedFullName = "openai/researchtrack",
        RequestTokenHash = Hash(RawToken),
        FlowType = GitHubInstallationFlowTypes.Requested,
        Status = GitHubRepositoryAccessRequestStatuses.Pending,
        CreatedAt = Now.AddMinutes(-1).UtcDateTime,
        ExpiresAt = Now.AddMinutes(10).UtcDateTime,
        Version = 0
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubRequestStore : IGitHubRepositoryAccessRequestStore
    {
        public GitHubRepositoryAccessRequest? Request { get; set; }

        public Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.CompletedTask;
        }

        public Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.Id == requestId ? Request : null);

        public Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(string requestTokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.RequestTokenHash == requestTokenHash ? Request : null);

        public Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(Guid projectId, string normalizedFullName, CancellationToken cancellationToken) =>
            Task.FromResult(new OwnerGrantProjectLinkState(0, 0, false));

        public Task<GitHubRepositoryAccessRequest?> TryFailAsync(Guid requestId, string failureCode, DateTime now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<GitHubRepositoryAccessRequest?> TryExpireAsync(Guid requestId, DateTime now, CancellationToken cancellationToken)
        {
            if (Request is not null
                && Request.Id == requestId
                && Request.Status == GitHubRepositoryAccessRequestStatuses.Pending
                && Request.ExpiresAt <= now)
            {
                Request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
                Request.ConsumedAt = now;
                Request.Version++;
                return Task.FromResult<GitHubRepositoryAccessRequest?>(Request);
            }
            return Task.FromResult<GitHubRepositoryAccessRequest?>(null);
        }
    }

    private sealed class StubStateService : IGitHubInstallationStateService
    {
        public bool CreateRequestedWasCalled { get; private set; }
        public Guid ProjectId { get; private set; }
        public Guid UserId { get; private set; }
        public Guid RequestId { get; private set; }
        public string? ReturnPath { get; private set; }

        public Task<GitHubInstallationState> CreateRequestedAsync(
            Guid projectId,
            Guid initiatingUserId,
            Guid repositoryAccessRequestId,
            string returnPath,
            CancellationToken cancellationToken)
        {
            CreateRequestedWasCalled = true;
            ProjectId = projectId;
            UserId = initiatingUserId;
            RequestId = repositoryAccessRequestId;
            ReturnPath = returnPath;
            return Task.FromResult(new GitHubInstallationState("raw-researchtrack-state", Now.AddMinutes(10).UtcDateTime));
        }

        public Task<GitHubInstallationState> CreateAsync(Guid projectId, Guid initiatingUserId, string flowType, string returnPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(string state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedGitHubInstallationState> ValidateAsync(string state, Guid expectedUserId, string expectedFlowType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(string state, string expectedFlowType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedGitHubInstallationState> BindInstallationAsync(string state, long installationId, string expectedFlowType, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ValidatedGitHubInstallationState> BindRequestedInstallationAsync(string state, Guid repositoryAccessRequestId, long installationId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConsumedGitHubInstallationState> ConsumeAsync(string state, Guid expectedUserId, string expectedFlowType, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
