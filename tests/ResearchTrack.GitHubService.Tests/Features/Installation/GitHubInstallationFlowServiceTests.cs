using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Installation;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Tests.Features.Installation;

public sealed class GitHubInstallationFlowServiceTests
{
    private static readonly Guid ProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid SourceId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const long InstallationId = 98765;
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 4, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_authorizes_project_and_returns_direct_install_url()
    {
        var authorization = new StubAuthorizationClient();
        var state = new StubStateService();
        var service = CreateService(authorization, state);

        var response = await service.StartAsync(
            UserId,
            new StartGitHubInstallationRequest(ProjectId, null),
            TestContext.Current.CancellationToken);

        Assert.True(authorization.WasCalled);
        Assert.True(state.CreateWasCalled);
        Assert.Equal(ProjectId, state.CreatedProjectId);
        Assert.Equal(UserId, state.CreatedUserId);
        Assert.Contains("github.com/apps/researchtrack-test/installations/new", response.GitHubAuthorizeUrl);
        Assert.Contains("state=state-value", response.GitHubAuthorizeUrl);
    }

    [Fact]
    public async Task Setup_callback_verifies_installation_consumes_state_and_persists_source()
    {
        var state = new StubStateService();
        var appClient = new StubGitHubAppClient();
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, store);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            InstallationId,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(state.ValidateWasCalled);
        Assert.True(appClient.GetInstallationWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.True(store.WasCalled);
        Assert.True(result.Succeeded);
        Assert.Equal(SourceId, result.SourceId);
        Assert.Equal(InstallationId, result.InstallationId);
        Assert.Null(result.ExternalRedirectUrl);
    }

    [Fact]
    public async Task Requested_callback_reuses_same_github_app_pipeline_and_completes_access_request()
    {
        var requestId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            AccessRequestId = requestId
        };
        var requests = new StubAccessRequestService { ResultToken = "result-token" };
        var appClient = new StubGitHubAppClient();
        var store = new StubInstallationStore { ExpectedAccessType = GitHubAccessTypes.InstallationRequested };
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            appClient,
            store,
            requests);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            InstallationId,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.True(result.Succeeded);
        Assert.True(appClient.GetInstallationWasCalled);
        Assert.True(store.WasCalled);
        Assert.True(requests.OwnerCheckWasCalled);
        Assert.True(requests.CompleteWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.Equal(GitHubInstallationFlowTypes.Requested, result.FlowType);
        Assert.Contains("result-token", result.ExternalRedirectUrl);
    }

    [Fact]
    public async Task Unexpected_user_oauth_callback_is_rejected_and_never_persisted()
    {
        var state = new StubStateService();
        var appClient = new StubGitHubAppClient();
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, store);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            null,
            null,
            "obsolete-oauth-code",
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("unexpected_user_oauth", result.ErrorCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
        Assert.False(store.WasCalled);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("denied")]
    public async Task Cancelled_or_denied_setup_consumes_state_and_creates_no_source(string action)
    {
        var state = new StubStateService();
        var appClient = new StubGitHubAppClient();
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, store);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            null,
            action,
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("cancelled", result.ErrorCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task Invalid_state_stops_before_trusting_installation()
    {
        var state = new StubStateService
        {
            ValidateFailure = new ApiException(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationError,
                "Invalid state")
        };
        var appClient = new StubGitHubAppClient();
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, store);

        await Assert.ThrowsAsync<ApiException>(() => service.CompleteCallbackAsync(
            "tampered-state",
            InstallationId,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken));

        Assert.False(appClient.GetInstallationWasCalled);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task Missing_installation_consumes_state_and_returns_safe_failure()
    {
        var state = new StubStateService();
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, store: store);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            null,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("missing_installation", result.ErrorCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(store.WasCalled);
    }

    [Fact]
    public async Task Installation_not_owned_by_app_returns_invalid_installation()
    {
        var state = new StubStateService();
        var appClient = new StubGitHubAppClient
        {
            Failure = new ApiException(
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "Not found")
        };
        var store = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, store);

        var result = await service.CompleteCallbackAsync(
            "state-value",
            InstallationId,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal("invalid_installation", result.ErrorCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(store.WasCalled);
    }

    private static GitHubInstallationFlowService CreateService(
        StubAuthorizationClient authorization,
        StubStateService state,
        StubGitHubAppClient? appClient = null,
        StubInstallationStore? store = null,
        StubAccessRequestService? accessRequests = null)
    {
        var options = new GitHubAppOptions(
            12345,
            "researchtrack-test",
            "Iv1.test-client",
            "test-client-secret",
            "/tmp/test.pem",
            new Uri("https://api.example.test/api/github/access-source/install/callback"),
            new Uri("https://app.example.test/"),
            TimeSpan.FromMinutes(10),
            TimeSpan.FromHours(24));

        return new GitHubInstallationFlowService(
            authorization,
            state,
            accessRequests ?? new StubAccessRequestService(),
            appClient ?? new StubGitHubAppClient(),
            store ?? new StubInstallationStore(),
            options,
            new FixedTimeProvider(Now),
            NullLogger<GitHubInstallationFlowService>.Instance);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubAuthorizationClient : IProjectAuthorizationClient
    {
        public Exception? Failure { get; init; }
        public bool WasCalled { get; private set; }

        public Task EnsureCanManageAsync(Guid projectId, CancellationToken cancellationToken)
        {
            WasCalled = true;
            Assert.Equal(ProjectId, projectId);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class StubStateService : IGitHubInstallationStateService
    {
        public Exception? ValidateFailure { get; init; }
        public long? PendingInstallationId { get; set; }
        public bool CreateWasCalled { get; private set; }
        public bool ValidateWasCalled { get; private set; }
        public bool ConsumeWasCalled { get; private set; }
        public Guid CreatedProjectId { get; private set; }
        public Guid CreatedUserId { get; private set; }
        public string FlowType { get; init; } = GitHubInstallationFlowTypes.Direct;
        public Guid? AccessRequestId { get; init; }

        public Task<GitHubInstallationState> CreateAsync(
            Guid projectId,
            Guid initiatingUserId,
            string flowType,
            string returnPath,
            CancellationToken cancellationToken,
            Guid? accessRequestId = null)
        {
            CreateWasCalled = true;
            CreatedProjectId = projectId;
            CreatedUserId = initiatingUserId;
            return Task.FromResult(new GitHubInstallationState(
                "state-value",
                Now.AddMinutes(10).UtcDateTime));
        }

        public Task<ValidatedGitHubInstallationState> ValidateAsync(
            string state,
            Guid expectedUserId,
            string expectedFlowType,
            CancellationToken cancellationToken) =>
            ValidateExternalCallbackAsync(state, cancellationToken);

        public Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
            string state,
            CancellationToken cancellationToken)
        {
            ValidateWasCalled = true;
            if (ValidateFailure is not null)
            {
                return Task.FromException<ValidatedGitHubInstallationState>(ValidateFailure);
            }

            return Task.FromResult(new ValidatedGitHubInstallationState(
                ProjectId,
                UserId,
                FlowType,
                string.Equals(FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal)
                    ? "/github/access-updated"
                    : $"/supervisor/projects/{ProjectId:D}",
                AccessRequestId,
                PendingInstallationId,
                PendingInstallationId.HasValue ? Now.UtcDateTime : null));
        }

        public Task<ValidatedGitHubInstallationState> BindInstallationAsync(
            string state,
            long installationId,
            string expectedFlowType,
            CancellationToken cancellationToken)
        {
            PendingInstallationId = installationId;
            return ValidateExternalCallbackAsync(state, cancellationToken);
        }

        public Task<ConsumedGitHubInstallationState> ConsumeAsync(
            string state,
            Guid expectedUserId,
            string expectedFlowType,
            CancellationToken cancellationToken)
        {
            ConsumeWasCalled = true;
            return Task.FromResult(new ConsumedGitHubInstallationState(
                ProjectId,
                UserId,
                FlowType,
                string.Equals(FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal)
                    ? "/github/access-updated"
                    : $"/supervisor/projects/{ProjectId:D}",
                AccessRequestId,
                PendingInstallationId,
                Now.UtcDateTime));
        }
    }

    private sealed class StubAccessRequestService : IGitHubAccessRequestService
    {
        public bool OwnerCheckWasCalled { get; private set; }
        public bool CompleteWasCalled { get; private set; }
        public string? ResultToken { get; init; }

        public Task<GitHubAccessRequestCreateResponse> CreateAsync(Guid userId, Guid projectId, string ownerLogin, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<GitHubAccessRequestSummaryResponse>> ListAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeAsync(Guid userId, Guid projectId, Guid requestId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GitHubAccessRequestValidationResponse> ValidateAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<PendingGitHubAccessRequest> ResolvePendingAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureInstallationOwnerAsync(Guid requestId, string ownerLogin, CancellationToken cancellationToken)
        {
            OwnerCheckWasCalled = true;
            Assert.Equal("openai", ownerLogin);
            return Task.CompletedTask;
        }
        public Task<string?> CompleteAsync(Guid requestId, Guid sourceId, long installationId, CancellationToken cancellationToken)
        {
            CompleteWasCalled = true;
            Assert.Equal(SourceId, sourceId);
            Assert.Equal(InstallationId, installationId);
            return Task.FromResult(ResultToken);
        }
        public Task<string?> FailAsync(Guid requestId, string errorCode, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        public Task<GitHubAccessUpdatedSummaryResponse> GetResultAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeAsync(string token, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GitHubAccessUpdatedSummaryResponse> GetLatestCompletedAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeLatestAsync(Guid userId, Guid projectId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubGitHubAppClient : IGitHubAppClient
    {
        public Exception? Failure { get; init; }
        public bool GetInstallationWasCalled { get; private set; }

        public Task<GitHubInstallationInfo> GetInstallationAsync(
            long installationId,
            CancellationToken cancellationToken)
        {
            GetInstallationWasCalled = true;
            return Failure is null
                ? Task.FromResult(new GitHubInstallationInfo(installationId, "openai", "ORG"))
                : Task.FromException<GitHubInstallationInfo>(Failure);
        }

        public Task<GitHubInstallationToken> CreateInstallationTokenAsync(
            long installationId,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GitHubInstallationToken(
                "ephemeral-installation-token",
                Now.AddMinutes(30).UtcDateTime));
    }

    private sealed class StubInstallationStore : IInstallationAccessSourceStore
    {
        public bool WasCalled { get; private set; }
        public string ExpectedAccessType { get; init; } = GitHubAccessTypes.InstallationDirect;

        public Task<Guid> CreateAsync(
            Guid projectId,
            Guid userId,
            GitHubInstallationInfo installation,
            string accessType,
            DateTime now,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            Assert.Equal(ProjectId, projectId);
            Assert.Equal(UserId, userId);
            Assert.Equal(InstallationId, installation.InstallationId);
            Assert.Equal(ExpectedAccessType, accessType);
            return Task.FromResult(SourceId);
        }
    }
}
