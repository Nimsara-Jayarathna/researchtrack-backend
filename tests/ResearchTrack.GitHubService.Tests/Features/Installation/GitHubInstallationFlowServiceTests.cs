using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
    private static readonly Guid RequestId = Guid.Parse("44444444-4444-4444-4444-444444444444");
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

    [Fact]
    public async Task Requested_callback_verifies_exact_repository_completes_and_consumes_state_without_direct_source_creation()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var appClient = new StubGitHubAppClient();
        var sourceStore = new StubInstallationStore();
        var repositoryService = new StubRequestedRepositoryService();
        var completionService = new StubCompletionService();
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            appClient,
            sourceStore,
            requestStore,
            repositoryService,
            completionService);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.True(state.ValidateWasCalled);
        Assert.True(state.BindRequestedWasCalled);
        Assert.True(appClient.GetInstallationWasCalled);
        Assert.True(repositoryService.VerifyRequestedWasCalled);
        Assert.True(completionService.WasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(sourceStore.WasCalled);
        Assert.True(result.Succeeded);
        Assert.Null(result.ErrorCode);
        Assert.Equal(SourceId, result.SourceId);
        Assert.Equal(RequestId, result.RepositoryAccessRequestId);
        Assert.Equal(GitHubInstallationFlowTypes.Requested, result.FlowType);
    }

    [Fact]
    public async Task Requested_duplicate_callback_is_rejected_by_consumed_state_before_second_completion()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId,
            RejectValidationAfterConsume = true
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var completionService = new StubCompletionService();
        var sourceStore = new StubInstallationStore();
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            store: sourceStore,
            requestStore: requestStore,
            installationRepositoryService: new StubRequestedRepositoryService(),
            completionService: completionService);

        var first = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.True(first.Succeeded);
        Assert.Equal(1, completionService.CallCount);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(sourceStore.WasCalled);

        var replay = await Assert.ThrowsAsync<ApiException>(() => service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status400BadRequest, replay.StatusCode);
        Assert.Equal(1, completionService.CallCount);
        Assert.False(sourceStore.WasCalled);
    }

    [Fact]
    public async Task Requested_callback_with_invalid_state_stops_before_request_or_installation_processing()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId,
            ValidateFailure = new ApiException(StatusCodes.Status400BadRequest, ErrorCodes.ValidationError, "Invalid state")
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var appClient = new StubGitHubAppClient();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, requestStore: requestStore);

        await Assert.ThrowsAsync<ApiException>(() => service.CompleteCallbackAsync(
            "tampered", InstallationId, "install", null, null, TestContext.Current.CancellationToken));

        Assert.False(state.BindRequestedWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
        Assert.False(requestStore.FailWasCalled);
    }

    [Fact]
    public async Task Requested_callback_project_mismatch_fails_request_and_never_trusts_installation()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var mismatched = PendingRequest();
        mismatched.ProjectId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var requestStore = new StubRequestStore { Request = mismatched };
        var appClient = new StubGitHubAppClient();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.Equal("request_context_mismatch", result.ErrorCode);
        Assert.True(requestStore.FailWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("denied")]
    public async Task Requested_callback_denial_marks_request_failed_and_creates_no_source(string setupAction)
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var sourceStore = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, store: sourceStore, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", null, setupAction, null, null, TestContext.Current.CancellationToken);

        Assert.Equal("cancelled", result.ErrorCode);
        Assert.True(requestStore.FailWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(sourceStore.WasCalled);
    }

    [Fact]
    public async Task Requested_callback_expired_request_is_marked_expired_before_installation_is_used()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var expired = PendingRequest();
        expired.ExpiresAt = Now.AddSeconds(-1).UtcDateTime;
        var requestStore = new StubRequestStore { Request = expired };
        var appClient = new StubGitHubAppClient();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.Equal("request_expired", result.ErrorCode);
        Assert.True(requestStore.ExpireWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
    }

    [Fact]
    public async Task Requested_callback_invalid_installation_fails_request_after_atomic_binding()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var appClient = new StubGitHubAppClient
        {
            Failure = new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Not found")
        };
        var sourceStore = new StubInstallationStore();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, sourceStore, requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.True(state.BindRequestedWasCalled);
        Assert.Equal("invalid_installation", result.ErrorCode);
        Assert.True(requestStore.FailWasCalled);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(sourceStore.WasCalled);
    }

    [Fact]
    public async Task Requested_callback_github_unavailable_keeps_request_pending_and_state_retryable()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var appClient = new StubGitHubAppClient
        {
            Failure = new ApiException(StatusCodes.Status503ServiceUnavailable, ErrorCodes.DependencyUnavailable, "Unavailable")
        };
        var service = CreateService(new StubAuthorizationClient(), state, appClient, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.Equal("github_unavailable", result.ErrorCode);
        Assert.False(requestStore.FailWasCalled);
        Assert.False(state.ConsumeWasCalled);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Pending, requestStore.Request!.Status);
    }

    [Theory]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Completed, GitHubRepositoryAccessFailureCodes.RequestCompleted)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Expired, GitHubRepositoryAccessFailureCodes.RequestExpired)]
    [InlineData(GitHubRepositoryAccessRequestStatuses.Failed, GitHubRepositoryAccessFailureCodes.InvalidInstallation)]
    public async Task Requested_terminal_request_is_immutable_and_never_reprocesses_installation(
        string terminalStatus,
        string expectedError)
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var request = PendingRequest();
        request.Status = terminalStatus;
        request.FailureCode = terminalStatus == GitHubRepositoryAccessRequestStatuses.Failed
            ? GitHubRepositoryAccessFailureCodes.InvalidInstallation
            : null;
        request.CompletedAt = terminalStatus == GitHubRepositoryAccessRequestStatuses.Completed
            ? Now.AddSeconds(-1).UtcDateTime
            : null;
        request.ConsumedAt = Now.AddSeconds(-1).UtcDateTime;
        var originalVersion = request.Version;
        var requestStore = new StubRequestStore { Request = request };
        var appClient = new StubGitHubAppClient();
        var repositoryService = new StubRequestedRepositoryService();
        var completionService = new StubCompletionService();
        var sourceStore = new StubInstallationStore();
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            appClient,
            sourceStore,
            requestStore,
            repositoryService,
            completionService);

        var result = await service.CompleteCallbackAsync(
            "terminal-state",
            InstallationId,
            "install",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(expectedError, result.ErrorCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
        Assert.False(repositoryService.VerifyRequestedWasCalled);
        Assert.False(completionService.WasCalled);
        Assert.False(sourceStore.WasCalled);
        Assert.False(requestStore.FailWasCalled);
        Assert.False(requestStore.ExpireWasCalled);
        Assert.Equal(terminalStatus, requestStore.Request!.Status);
        Assert.Equal(originalVersion, requestStore.Request.Version);
    }

    [Fact]
    public async Task Requested_unknown_github_error_is_mapped_to_safe_failure_code()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var service = CreateService(new StubAuthorizationClient(), state, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state",
            null,
            null,
            null,
            "provider-secret-diagnostic-value",
            TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.GitHubAuthorizationFailed, result.ErrorCode);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.GitHubAuthorizationFailed, requestStore.FailureCode);
        Assert.DoesNotContain("provider-secret", requestStore.FailureCode!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Requested_revoked_repository_access_fails_without_completion_or_direct_source_creation()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var repositoryService = new StubRequestedRepositoryService
        {
            Failure = new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Repository unavailable")
        };
        var completionService = new StubCompletionService();
        var sourceStore = new StubInstallationStore();
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            new StubGitHubAppClient(),
            sourceStore,
            requestStore,
            repositoryService,
            completionService);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "install", null, null, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.RepositoryNotAccessible, result.ErrorCode);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Failed, requestStore.Request!.Status);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.RepositoryNotAccessible, requestStore.Request.FailureCode);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(completionService.WasCalled);
        Assert.False(sourceStore.WasCalled);
    }

    [Fact]
    public async Task Requested_malformed_setup_action_fails_before_installation_lookup()
    {
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var appClient = new StubGitHubAppClient();
        var service = CreateService(new StubAuthorizationClient(), state, appClient, requestStore: requestStore);

        var result = await service.CompleteCallbackAsync(
            "requested-state", InstallationId, "unexpected-action", null, null, TestContext.Current.CancellationToken);

        Assert.False(result.Succeeded);
        Assert.Equal(GitHubRepositoryAccessFailureCodes.InvalidSetupAction, result.ErrorCode);
        Assert.Equal(GitHubRepositoryAccessRequestStatuses.Failed, requestStore.Request!.Status);
        Assert.True(state.ConsumeWasCalled);
        Assert.False(appClient.GetInstallationWasCalled);
    }

    [Fact]
    public async Task Requested_audit_logs_never_include_raw_state_authorization_code_or_provider_error()
    {
        const string rawState = "raw-owner-state-secret";
        const string rawCode = "raw-github-authorization-code-secret";
        const string rawError = "raw-provider-error-secret";
        var logger = new CaptureLogger<GitHubInstallationFlowService>();
        var state = new StubStateService
        {
            FlowType = GitHubInstallationFlowTypes.Requested,
            RepositoryAccessRequestId = RequestId
        };
        var requestStore = new StubRequestStore { Request = PendingRequest() };
        var service = CreateService(
            new StubAuthorizationClient(),
            state,
            requestStore: requestStore,
            logger: logger);

        await service.CompleteCallbackAsync(
            rawState,
            null,
            null,
            rawCode,
            rawError,
            TestContext.Current.CancellationToken);

        var rendered = string.Join("\n", logger.Messages);
        Assert.DoesNotContain(rawState, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(rawCode, rendered, StringComparison.Ordinal);
        Assert.DoesNotContain(rawError, rendered, StringComparison.Ordinal);
        Assert.Contains(RequestId.ToString("D"), rendered, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubRepositoryAccessRequest PendingRequest() => new()
    {
        Id = RequestId,
        ProjectId = ProjectId,
        InitiatingUserId = UserId,
        RequestedOwner = "openai",
        RequestedRepositoryName = "researchtrack",
        RequestedFullName = "openai/researchtrack",
        RequestTokenHash = new string('a', 64),
        FlowType = GitHubInstallationFlowTypes.Requested,
        Status = GitHubRepositoryAccessRequestStatuses.Pending,
        CreatedAt = Now.AddMinutes(-1).UtcDateTime,
        ExpiresAt = Now.AddMinutes(20).UtcDateTime,
        Version = 0
    };

    private static GitHubInstallationFlowService CreateService(
        StubAuthorizationClient authorization,
        StubStateService state,
        StubGitHubAppClient? appClient = null,
        StubInstallationStore? store = null,
        StubRequestStore? requestStore = null,
        StubRequestedRepositoryService? installationRepositoryService = null,
        StubCompletionService? completionService = null,
        ILogger<GitHubInstallationFlowService>? logger = null)
    {
        var options = new GitHubAppOptions(
            12345,
            "researchtrack-test",
            "Iv1.test-client",
            "test-client-secret",
            "/tmp/test.pem",
            new Uri("https://api.example.test/api/github/access-source/install/callback"),
            new Uri("https://app.example.test/"),
            TimeSpan.FromMinutes(10));

        return new GitHubInstallationFlowService(
            authorization,
            state,
            appClient ?? new StubGitHubAppClient(),
            store ?? new StubInstallationStore(),
            requestStore ?? new StubRequestStore(),
            installationRepositoryService ?? new StubRequestedRepositoryService(),
            completionService ?? new StubCompletionService(),
            options,
            new FixedTimeProvider(Now),
            logger ?? NullLogger<GitHubInstallationFlowService>.Instance);
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
        public bool RejectValidationAfterConsume { get; init; }
        public long? PendingInstallationId { get; set; }
        public string FlowType { get; set; } = GitHubInstallationFlowTypes.Direct;
        public Guid? RepositoryAccessRequestId { get; set; }
        public bool BindRequestedWasCalled { get; private set; }
        public bool CreateWasCalled { get; private set; }
        public bool ValidateWasCalled { get; private set; }
        public bool ConsumeWasCalled { get; private set; }
        public Guid CreatedProjectId { get; private set; }
        public Guid CreatedUserId { get; private set; }

        public Task<GitHubInstallationState> CreateAsync(
            Guid projectId,
            Guid initiatingUserId,
            string flowType,
            string returnPath,
            CancellationToken cancellationToken)
        {
            CreateWasCalled = true;
            CreatedProjectId = projectId;
            CreatedUserId = initiatingUserId;
            return Task.FromResult(new GitHubInstallationState(
                "state-value",
                Now.AddMinutes(10).UtcDateTime));
        }

        public Task<GitHubInstallationState> CreateRequestedAsync(
            Guid projectId,
            Guid initiatingUserId,
            Guid repositoryAccessRequestId,
            string returnPath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new GitHubInstallationState("requested-state", Now.AddMinutes(10).UtcDateTime));

        public Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
            string state,
            CancellationToken cancellationToken) =>
            ValidateExternalCallbackAsync(state, FlowType, cancellationToken);

        public Task<ValidatedGitHubInstallationState> ValidateAsync(
            string state,
            Guid expectedUserId,
            string expectedFlowType,
            CancellationToken cancellationToken) =>
            ValidateExternalCallbackAsync(state, expectedFlowType, cancellationToken);

        public Task<ValidatedGitHubInstallationState> ValidateExternalCallbackAsync(
            string state,
            string expectedFlowType,
            CancellationToken cancellationToken)
        {
            ValidateWasCalled = true;
            if (RejectValidationAfterConsume && ConsumeWasCalled)
            {
                return Task.FromException<ValidatedGitHubInstallationState>(new ApiException(
                    StatusCodes.Status400BadRequest,
                    ErrorCodes.ValidationError,
                    "GitHub installation state has already been consumed."));
            }
            if (ValidateFailure is not null)
            {
                return Task.FromException<ValidatedGitHubInstallationState>(ValidateFailure);
            }

            return Task.FromResult(new ValidatedGitHubInstallationState(
                ProjectId,
                UserId,
                FlowType,
                FlowType == GitHubInstallationFlowTypes.Requested ? "/github/access-updated" : $"/supervisor/projects/{ProjectId:D}",
                RepositoryAccessRequestId,
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
            return ValidateExternalCallbackAsync(state, expectedFlowType, cancellationToken);
        }

        public Task<ValidatedGitHubInstallationState> BindRequestedInstallationAsync(
            string state,
            Guid repositoryAccessRequestId,
            long installationId,
            CancellationToken cancellationToken)
        {
            BindRequestedWasCalled = true;
            PendingInstallationId = installationId;
            FlowType = GitHubInstallationFlowTypes.Requested;
            RepositoryAccessRequestId = repositoryAccessRequestId;
            return ValidateExternalCallbackAsync(state, GitHubInstallationFlowTypes.Requested, cancellationToken);
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
                FlowType == GitHubInstallationFlowTypes.Requested ? "/github/access-updated" : $"/supervisor/projects/{ProjectId:D}",
                RepositoryAccessRequestId,
                PendingInstallationId,
                Now.UtcDateTime));
        }
    }

    private sealed class StubRequestStore : IGitHubRepositoryAccessRequestStore
    {
        public GitHubRepositoryAccessRequest? Request { get; set; }
        public bool FailWasCalled { get; private set; }
        public bool ExpireWasCalled { get; private set; }
        public string? FailureCode { get; private set; }

        public Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.CompletedTask;
        }

        public Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.Id == requestId ? Request : null);

        public Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(string requestTokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Request?.RequestTokenHash == requestTokenHash ? Request : null);

        public Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(
            Guid projectId,
            string normalizedFullName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new OwnerGrantProjectLinkState(0, 0, false));

        public Task<GitHubRepositoryAccessRequest?> TryFailAsync(Guid requestId, string failureCode, DateTime now, CancellationToken cancellationToken)
        {
            FailWasCalled = true;
            FailureCode = failureCode;
            if (Request is not null && Request.Id == requestId)
            {
                Request.Status = GitHubRepositoryAccessRequestStatuses.Failed;
                Request.FailureCode = failureCode;
            }
            return Task.FromResult(Request);
        }

        public Task<GitHubRepositoryAccessRequest?> TryExpireAsync(Guid requestId, DateTime now, CancellationToken cancellationToken)
        {
            ExpireWasCalled = true;
            if (Request is not null && Request.Id == requestId)
            {
                Request.Status = GitHubRepositoryAccessRequestStatuses.Expired;
            }
            return Task.FromResult(Request);
        }
    }

    private sealed class StubRequestedRepositoryService : IGitHubInstallationRepositoryService
    {
        public Exception? Failure { get; init; }
        public bool VerifyRequestedWasCalled { get; private set; }

        public Task<GitHubAvailableRepositoriesResponse?> TryGetAvailableAsync(
            Guid userId,
            Guid sourceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubInstallationRepositoriesPageResponse> GetInstallationPageAsync(
            Guid userId,
            Guid projectId,
            long installationId,
            int page,
            int size,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LegacyInstallationRepositorySelection> ResolveLegacySelectionAsync(
            Guid userId,
            Guid projectId,
            long installationId,
            long repositoryId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> TryVerifyForLinkAsync(
            Guid userId,
            Guid projectId,
            Guid sourceId,
            IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<GitHubInstallationRepository> VerifyRequestedRepositoryAsync(
            long installationId,
            string requestedOwner,
            string requestedRepositoryName,
            CancellationToken cancellationToken)
        {
            VerifyRequestedWasCalled = true;
            Assert.Equal(InstallationId, installationId);
            Assert.Equal("openai", requestedOwner);
            Assert.Equal("researchtrack", requestedRepositoryName);
            return Failure is null
                ? Task.FromResult(new GitHubInstallationRepository(
                    123456,
                    "openai",
                    "researchtrack",
                    "openai/researchtrack",
                    "https://github.com/openai/researchtrack",
                    "main",
                    true))
                : Task.FromException<GitHubInstallationRepository>(Failure);
        }
    }

    private sealed class StubCompletionService : IGitHubRepositoryAccessCompletionService
    {
        public Exception? Failure { get; init; }
        public bool WasCalled { get; private set; }
        public int CallCount { get; private set; }
        public GitHubRepositoryAccessCompletionResult? Result { get; init; }

        public Task<GitHubRepositoryAccessCompletionResult> CompleteAsync(
            Guid requestId,
            GitHubInstallationInfo installation,
            GitHubInstallationRepository repository,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            CallCount++;
            if (Failure is not null)
            {
                return Task.FromException<GitHubRepositoryAccessCompletionResult>(Failure);
            }

            return Task.FromResult(Result ?? new GitHubRepositoryAccessCompletionResult(
                requestId,
                ProjectId,
                true,
                false,
                null,
                SourceId,
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                repository.Id,
                true));
        }
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

    private sealed class CaptureLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class StubInstallationStore : IInstallationAccessSourceStore
    {
        public bool WasCalled { get; private set; }

        public Task<Guid> CreateAsync(
            Guid projectId,
            Guid userId,
            GitHubInstallationInfo installation,
            DateTime now,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            Assert.Equal(ProjectId, projectId);
            Assert.Equal(UserId, userId);
            Assert.Equal(InstallationId, installation.InstallationId);
            return Task.FromResult(SourceId);
        }
    }
}
