using ResearchTrack.BuildingBlocks.Api.Contracts;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubInstallationFlowService : IGitHubInstallationFlowService
{
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IGitHubInstallationStateService _stateService;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly IInstallationAccessSourceStore _accessSourceStore;
    private readonly IGitHubRepositoryAccessRequestStore _requestStore;
    private readonly IGitHubInstallationRepositoryService _installationRepositoryService;
    private readonly IGitHubRepositoryAccessCompletionService _completionService;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationFlowService> _logger;

    public GitHubInstallationFlowService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubInstallationStateService stateService,
        IGitHubAppClient gitHubAppClient,
        IInstallationAccessSourceStore accessSourceStore,
        IGitHubRepositoryAccessRequestStore requestStore,
        IGitHubInstallationRepositoryService installationRepositoryService,
        IGitHubRepositoryAccessCompletionService completionService,
        GitHubAppOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationFlowService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _stateService = stateService;
        _gitHubAppClient = gitHubAppClient;
        _accessSourceStore = accessSourceStore;
        _requestStore = requestStore;
        _installationRepositoryService = installationRepositoryService;
        _completionService = completionService;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubInstallStartResponse> StartAsync(
        Guid userId,
        StartGitHubInstallationRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RequestToken))
        {
            throw new ApiValidationException([
                new ApiFieldError(
                    "requestToken",
                    ["Requested-access installation is handled by its existing request flow."])
            ]);
        }

        if (request.ProjectId is null || request.ProjectId == Guid.Empty)
        {
            throw new ApiValidationException([
                new ApiFieldError("projectId", ["Project id is required."])
            ]);
        }

        var projectId = request.ProjectId.Value;
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);

        var state = await _stateService.CreateAsync(
            projectId,
            userId,
            GitHubInstallationFlowTypes.Direct,
            $"/supervisor/projects/{projectId:D}",
            cancellationToken);

        var installUrl = GitHubInstallationUrlBuilder.Build(_options.AppSlug, state.Value);
        _logger.LogInformation(
            "GitHub App installation flow started. ProjectId={ProjectId} UserId={UserId}",
            projectId,
            userId);

        return new GitHubInstallStartResponse(
            projectId,
            installUrl.AbsoluteUri,
            GitHubInstallationFlowTypes.Direct,
            state.ExpiresAt);
    }

    public async Task<GitHubInstallationCallbackResult> CompleteCallbackAsync(
        string? state,
        long? installationId,
        string? setupAction,
        string? code,
        string? error,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new ApiValidationException([
                new ApiFieldError("state", ["GitHub installation state is required."])
            ]);
        }

        var normalizedState = state.Trim();
        // Validate the ResearchTrack state before interpreting installation_id or any other GitHub value.
        // The flow type and request binding are recovered exclusively from the persisted state row.
        var validated = await _stateService.ValidateExternalCallbackAsync(
            normalizedState,
            cancellationToken);

        return validated.FlowType switch
        {
            GitHubInstallationFlowTypes.Direct => await CompleteDirectCallbackAsync(
                normalizedState, validated, installationId, setupAction, code, error, cancellationToken),
            GitHubInstallationFlowTypes.Requested => await CompleteRequestedCallbackAsync(
                normalizedState, validated, installationId, setupAction, code, error, cancellationToken),
            _ => throw new ApiException(
                StatusCodes.Status400BadRequest,
                ResearchTrack.BuildingBlocks.Api.Constants.ErrorCodes.ValidationError,
                "GitHub installation state has an unsupported flow type.")
        };
    }

    private async Task<GitHubInstallationCallbackResult> CompleteDirectCallbackAsync(
        string state,
        ValidatedGitHubInstallationState validated,
        long? installationId,
        string? setupAction,
        string? code,
        string? error,
        CancellationToken cancellationToken)
    {
        var normalizedAction = setupAction?.Trim().ToLowerInvariant();
        var normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(code))
        {
            var consumedLegacy = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            _logger.LogWarning(
                "Unexpected GitHub user OAuth callback received. ProjectId={ProjectId}",
                consumedLegacy.ProjectId);
            return Failure(consumedLegacy, GitHubRepositoryAccessFailureCodes.UnexpectedUserOAuth);
        }

        if (normalizedError is not null || IsCancelledOrDenied(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            var errorCode = normalizedError == "access_denied" || IsCancelledOrDenied(normalizedAction)
                ? GitHubRepositoryAccessFailureCodes.Cancelled
                : GitHubRepositoryAccessFailureCodes.GitHubAuthorizationFailed;
            return Failure(consumed, errorCode);
        }

        if (installationId is null || installationId <= 0)
        {
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            return Failure(consumed, GitHubRepositoryAccessFailureCodes.MissingInstallation);
        }

        if (!IsSupportedSetupAction(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            return Failure(consumed, GitHubRepositoryAccessFailureCodes.InvalidSetupAction);
        }

        try
        {
            var installation = await _gitHubAppClient.GetInstallationAsync(
                installationId.Value,
                cancellationToken);
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            var sourceId = await _accessSourceStore.CreateAsync(
                consumed.ProjectId,
                consumed.InitiatingUserId,
                installation,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            return new GitHubInstallationCallbackResult(
                consumed.ProjectId,
                sourceId,
                installation.InstallationId,
                consumed.FlowType,
                consumed.ReturnPath,
                true,
                null);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            return Failure(consumed, GitHubRepositoryAccessFailureCodes.InvalidInstallation);
        }
        catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var consumed = await ConsumeValidatedStateAsync(state, validated, cancellationToken);
            return Failure(consumed, GitHubRepositoryAccessFailureCodes.GitHubUnavailable);
        }
    }

    private async Task<GitHubInstallationCallbackResult> CompleteRequestedCallbackAsync(
        string state,
        ValidatedGitHubInstallationState validated,
        long? installationId,
        string? setupAction,
        string? code,
        string? error,
        CancellationToken cancellationToken)
    {
        if (validated.RepositoryAccessRequestId is not Guid requestId || requestId == Guid.Empty)
        {
            throw InvalidRequestedContext("Owner-granted GitHub state is not bound to a request.");
        }

        var request = await _requestStore.FindByIdAsync(requestId, cancellationToken);
        if (request is null)
        {
            throw InvalidRequestedContext("Owner-granted GitHub request no longer exists.");
        }

        if (request.ProjectId != validated.ProjectId
            || request.InitiatingUserId != validated.InitiatingUserId
            || !string.Equals(request.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.RequestContextMismatch, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.RequestContextMismatch);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (request.ExpiresAt <= now)
        {
            await _requestStore.TryExpireAsync(requestId, now, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.RequestExpired);
        }

        if (!string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GetTerminalFailureCode(request));
        }

        var normalizedAction = setupAction?.Trim().ToLowerInvariant();
        var normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(code))
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.UnexpectedUserOAuth, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.UnexpectedUserOAuth);
        }

        if (normalizedError is not null || IsCancelledOrDenied(normalizedAction))
        {
            var errorCode = normalizedError == "access_denied" || IsCancelledOrDenied(normalizedAction)
                ? GitHubRepositoryAccessFailureCodes.Cancelled
                : GitHubRepositoryAccessFailureCodes.GitHubAuthorizationFailed;
            await FailRequestedAsync(requestId, errorCode, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, errorCode);
        }

        if (installationId is null || installationId <= 0)
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.MissingInstallation, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.MissingInstallation);
        }

        if (!IsSupportedSetupAction(normalizedAction))
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.InvalidSetupAction, cancellationToken);
            await ConsumeRequestedStateAsync(state, validated, cancellationToken);
            return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.InvalidSetupAction);
        }

        // Bind both the ResearchTrack state and persistent request to the same installation in one DB transaction.
        // The store also materializes request expiry atomically if the deadline passes after the
        // callback pre-check but before this binding transaction.
        ValidatedGitHubInstallationState bound;
        try
        {
            bound = await _stateService.BindRequestedInstallationAsync(
                state,
                requestId,
                installationId.Value,
                cancellationToken);
        }
        catch (ApiException)
        {
            var latest = await _requestStore.FindByIdAsync(requestId, cancellationToken);
            if (latest is not null
                && string.Equals(latest.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal))
            {
                LogRequestedTerminalOutcome(latest, GitHubRepositoryAccessFailureCodes.RequestExpired, installationId);
                return RequestedFailure(validated, requestId, GitHubRepositoryAccessFailureCodes.RequestExpired);
            }

            throw;
        }
        EnsureSameRequestedContext(validated, bound, requestId, installationId.Value);

        GitHubInstallationInfo installation;
        try
        {
            // Current SCRUM-15 runtime security contract verifies the installation using the App identity.
            // OAuth/PKCE clients remain in the codebase but were intentionally removed from the active direct
            // flow in fc856269; requested flow therefore does not invent a divergent OAuth leg here.
            installation = await _gitHubAppClient.GetInstallationAsync(
                installationId.Value,
                cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.InvalidInstallation, cancellationToken);
            await ConsumeRequestedStateAsync(state, bound, cancellationToken);
            return RequestedFailure(bound, requestId, GitHubRepositoryAccessFailureCodes.InvalidInstallation);
        }
        catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            // Dependency outage is retryable: keep the request pending and state unconsumed, already bound
            // to the same installation so a retry cannot switch installation identity.
            return RequestedFailure(bound, requestId, GitHubRepositoryAccessFailureCodes.GitHubUnavailable);
        }

        GitHubInstallationRepository authoritativeRepository;
        try
        {
            // Never trust a repository supplied by the browser. Enumerate through the verified
            // installation token and match the exact repository identity persisted on the request.
            authoritativeRepository = await _installationRepositoryService.VerifyRequestedRepositoryAsync(
                installation.InstallationId,
                request.RequestedOwner,
                request.RequestedRepositoryName,
                cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            await FailRequestedAsync(requestId, GitHubRepositoryAccessFailureCodes.RepositoryNotAccessible, cancellationToken);
            await ConsumeRequestedStateAsync(state, bound, cancellationToken);
            return RequestedFailure(bound, requestId, GitHubRepositoryAccessFailureCodes.RepositoryNotAccessible);
        }
        catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            return RequestedFailure(bound, requestId, GitHubRepositoryAccessFailureCodes.GitHubUnavailable);
        }

        GitHubRepositoryAccessCompletionResult completion;
        try
        {
            completion = await _completionService.CompleteAsync(
                requestId,
                installation,
                authoritativeRepository,
                cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status409Conflict)
        {
            // Project-level repository mutation is serialized in the database. A lock conflict is
            // retryable and must not consume or fail an otherwise valid owner-grant request.
            return RequestedFailure(bound, requestId, GitHubRepositoryAccessFailureCodes.CompletionInProgress);
        }

        if (!completion.Succeeded)
        {
            await ConsumeRequestedStateAsync(state, bound, cancellationToken);
            return RequestedFailure(bound, requestId, completion.ErrorCode ?? GitHubRepositoryAccessFailureCodes.CompletionFailed);
        }

        await ConsumeRequestedStateAsync(state, bound, cancellationToken);
        _logger.LogInformation(
            "Owner-granted GitHub repository connection completed. RequestId={RequestId} ProjectId={ProjectId} InstallationId={InstallationId} GitHubRepositoryId={GitHubRepositoryId} AlreadyCompleted={AlreadyCompleted} InitialSyncHandoffSucceeded={InitialSyncHandoffSucceeded}",
            requestId,
            completion.ProjectId,
            installation.InstallationId,
            completion.GitHubRepositoryId,
            completion.AlreadyCompleted,
            completion.InitialSyncHandoffSucceeded);

        return new GitHubInstallationCallbackResult(
            completion.ProjectId,
            completion.SourceId,
            installation.InstallationId,
            validated.FlowType,
            validated.ReturnPath,
            true,
            null,
            RepositoryAccessRequestId: requestId);
    }

    private async Task FailRequestedAsync(Guid requestId, string errorCode, CancellationToken cancellationToken)
    {
        GitHubRepositoryAccessFailureCodes.RequireSafe(errorCode);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var failed = await _requestStore.TryFailAsync(
            requestId,
            errorCode,
            now,
            cancellationToken);
        if (failed is not null)
        {
            LogRequestedTerminalOutcome(failed, errorCode, failed.PendingInstallationId);
            return;
        }

        // Expiry wins over a concurrent failure. Never overwrite COMPLETED/FAILED/EXPIRED.
        var expired = await _requestStore.TryExpireAsync(requestId, now, cancellationToken);
        if (expired is not null)
        {
            LogRequestedTerminalOutcome(
                expired,
                GitHubRepositoryAccessFailureCodes.RequestExpired,
                expired.PendingInstallationId);
        }
    }

    private void LogRequestedTerminalOutcome(
        GitHubRepositoryAccessRequest request,
        string errorCode,
        long? installationId)
    {
        _logger.LogInformation(
            "Owner-granted GitHub request reached a terminal non-success outcome. RequestId={RequestId} ProjectId={ProjectId} Status={Status} FailureCode={FailureCode} InstallationId={InstallationId}",
            request.Id,
            request.ProjectId,
            request.Status,
            errorCode,
            installationId);
    }

    private static string GetTerminalFailureCode(GitHubRepositoryAccessRequest request)
    {
        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal))
        {
            return GitHubRepositoryAccessFailureCodes.RequestExpired;
        }
        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Completed, StringComparison.Ordinal))
        {
            return GitHubRepositoryAccessFailureCodes.RequestCompleted;
        }
        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Failed, StringComparison.Ordinal))
        {
            return GitHubRepositoryAccessFailureCodes.IsSafe(request.FailureCode)
                ? request.FailureCode!
                : GitHubRepositoryAccessFailureCodes.RequestFailed;
        }

        return GitHubRepositoryAccessFailureCodes.RequestNotPending;
    }

    private async Task<ConsumedGitHubInstallationState> ConsumeValidatedStateAsync(
        string state,
        ValidatedGitHubInstallationState validated,
        CancellationToken cancellationToken)
    {
        var consumed = await _stateService.ConsumeAsync(
            state,
            validated.InitiatingUserId,
            GitHubInstallationFlowTypes.Direct,
            cancellationToken);
        EnsureSameContext(validated, consumed);
        return consumed;
    }

    private async Task<ConsumedGitHubInstallationState> ConsumeRequestedStateAsync(
        string state,
        ValidatedGitHubInstallationState validated,
        CancellationToken cancellationToken)
    {
        var consumed = await _stateService.ConsumeAsync(
            state,
            validated.InitiatingUserId,
            GitHubInstallationFlowTypes.Requested,
            cancellationToken);
        EnsureSameContext(validated, consumed);
        return consumed;
    }

    private static void EnsureSameContext(
        ValidatedGitHubInstallationState validated,
        ConsumedGitHubInstallationState consumed)
    {
        if (consumed.ProjectId != validated.ProjectId
            || consumed.InitiatingUserId != validated.InitiatingUserId
            || !string.Equals(consumed.FlowType, validated.FlowType, StringComparison.Ordinal)
            || !string.Equals(consumed.ReturnPath, validated.ReturnPath, StringComparison.Ordinal)
            || consumed.RepositoryAccessRequestId != validated.RepositoryAccessRequestId
            || (validated.PendingInstallationId is long installation
                && consumed.PendingInstallationId != installation))
        {
            throw new InvalidOperationException(
                "GitHub installation state changed unexpectedly during callback validation.");
        }
    }

    private static void EnsureSameRequestedContext(
        ValidatedGitHubInstallationState validated,
        ValidatedGitHubInstallationState bound,
        Guid requestId,
        long installationId)
    {
        if (bound.ProjectId != validated.ProjectId
            || bound.InitiatingUserId != validated.InitiatingUserId
            || !string.Equals(bound.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal)
            || bound.RepositoryAccessRequestId != requestId
            || bound.PendingInstallationId != installationId)
        {
            throw new InvalidOperationException(
                "Owner-granted GitHub installation state changed unexpectedly while binding the installation.");
        }
    }

    private static GitHubInstallationCallbackResult Failure(
        ConsumedGitHubInstallationState state,
        string errorCode) => new(
            state.ProjectId,
            null,
            state.PendingInstallationId,
            state.FlowType,
            state.ReturnPath,
            false,
            errorCode);

    private static GitHubInstallationCallbackResult RequestedFailure(
        ValidatedGitHubInstallationState state,
        Guid requestId,
        string errorCode) => new(
            state.ProjectId,
            null,
            state.PendingInstallationId,
            state.FlowType,
            state.ReturnPath,
            false,
            errorCode,
            RepositoryAccessRequestId: requestId);

    private static bool IsCancelledOrDenied(string? setupAction) =>
        setupAction is "cancel" or GitHubRepositoryAccessFailureCodes.Cancelled or "denied";

    private static bool IsSupportedSetupAction(string? setupAction) =>
        setupAction is null or "" or "install" or "update";

    private static ApiException InvalidRequestedContext(string message) => new(
        StatusCodes.Status400BadRequest,
        ResearchTrack.BuildingBlocks.Api.Constants.ErrorCodes.ValidationError,
        message);
}
