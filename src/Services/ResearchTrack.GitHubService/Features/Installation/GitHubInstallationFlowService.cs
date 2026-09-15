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
    private readonly IGitHubAccessRequestService _accessRequests;
    private readonly IGitHubAppClient _gitHubAppClient;
    private readonly IInstallationAccessSourceStore _accessSourceStore;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationFlowService> _logger;

    public GitHubInstallationFlowService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubInstallationStateService stateService,
        IGitHubAccessRequestService accessRequests,
        IGitHubAppClient gitHubAppClient,
        IInstallationAccessSourceStore accessSourceStore,
        GitHubAppOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationFlowService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _stateService = stateService;
        _accessRequests = accessRequests;
        _gitHubAppClient = gitHubAppClient;
        _accessSourceStore = accessSourceStore;
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
            return await StartRequestedAsync(request.RequestToken, cancellationToken);
        }
        if (userId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "UNAUTHORIZED", "Authentication is required.");
        }
        if (request.ProjectId is null || request.ProjectId == Guid.Empty)
        {
            throw new ApiValidationException([new ApiFieldError("projectId", ["Project id is required."])]);
        }

        var projectId = request.ProjectId.Value;
        await _projectAuthorization.EnsureCanManageAsync(projectId, cancellationToken);
        var state = await _stateService.CreateAsync(
            projectId,
            userId,
            GitHubInstallationFlowTypes.Direct,
            $"/supervisor/projects/{projectId:D}",
            cancellationToken);
        return new GitHubInstallStartResponse(
            projectId,
            BuildInstallUrl(state.Value).AbsoluteUri,
            GitHubInstallationFlowTypes.Direct,
            state.ExpiresAt);
    }

    private async Task<GitHubInstallStartResponse> StartRequestedAsync(
        string requestToken,
        CancellationToken cancellationToken)
    {
        var request = await _accessRequests.ResolvePendingAsync(requestToken, cancellationToken);
        var state = await _stateService.CreateAsync(
            request.ProjectId,
            request.RequestedByUserId,
            GitHubInstallationFlowTypes.Requested,
            "/github/access-updated",
            cancellationToken,
            request.Id);
        _logger.LogInformation(
            "Requested GitHub App installation flow started. ProjectId={ProjectId} AccessRequestId={AccessRequestId}",
            request.ProjectId,
            request.Id);
        return new GitHubInstallStartResponse(
            request.ProjectId,
            BuildInstallUrl(state.Value).AbsoluteUri,
            GitHubInstallationFlowTypes.Requested,
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
            throw new ApiValidationException([new ApiFieldError("state", ["GitHub installation state is required."])]);
        }
        var normalizedState = state.Trim();
        var normalizedAction = setupAction?.Trim().ToLowerInvariant();
        var normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim().ToLowerInvariant();
        var validated = await _stateService.ValidateExternalCallbackAsync(normalizedState, cancellationToken);

        if (!string.IsNullOrWhiteSpace(code))
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "unexpected_user_oauth", cancellationToken);
        }
        if (normalizedError is not null || IsCancelledOrDenied(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            var errorCode = normalizedError == "access_denied" || IsCancelledOrDenied(normalizedAction)
                ? "cancelled"
                : "github_authorization_failed";
            return await FailureAsync(consumed, errorCode, cancellationToken);
        }
        if (installationId is null || installationId <= 0)
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "missing_installation", cancellationToken);
        }
        if (!IsSupportedSetupAction(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "invalid_setup_action", cancellationToken);
        }

        validated = await _stateService.BindInstallationAsync(
            normalizedState,
            installationId.Value,
            validated.FlowType,
            cancellationToken);

        GitHubInstallationInfo installation;
        try
        {
            installation = await _gitHubAppClient.GetInstallationAsync(installationId.Value, cancellationToken);
            if (string.Equals(validated.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
            {
                if (validated.AccessRequestId is not Guid requestId)
                {
                    throw new InvalidOperationException("Requested installation state is missing its access request binding.");
                }
                await _accessRequests.EnsureInstallationOwnerAsync(requestId, installation.OwnerLogin, cancellationToken);
            }
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status409Conflict
            && string.Equals(validated.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "installation_owner_mismatch", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "invalid_installation", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var consumed = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
            return await FailureAsync(consumed, "github_unavailable", cancellationToken);
        }

        // Persist the verified installation before consuming state. Both source creation
        // and request completion are idempotent, so a transient failure can safely retry
        // the callback while the state is still valid instead of stranding a request.
        var accessType = GitHubAccessTypes.FromFlowType(validated.FlowType);
        var sourceId = await _accessSourceStore.CreateAsync(
            validated.ProjectId,
            validated.InitiatingUserId,
            installation,
            accessType,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);

        string? requestedResultToken = null;
        if (string.Equals(validated.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            if (validated.AccessRequestId is not Guid requestId)
            {
                throw new InvalidOperationException("Requested installation state is missing its access request binding.");
            }
            requestedResultToken = await _accessRequests.CompleteAsync(
                requestId,
                sourceId,
                installation.InstallationId,
                cancellationToken);
        }

        var consumedState = await ConsumeValidatedStateAsync(normalizedState, validated, cancellationToken);
        string? externalRedirect = null;
        if (!string.IsNullOrWhiteSpace(requestedResultToken))
        {
            externalRedirect = BuildRequestedResultUrl(requestedResultToken, true, null);
        }

        _logger.LogInformation(
            "GitHub App installation connected. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId} FlowType={FlowType}",
            consumedState.ProjectId,
            sourceId,
            installation.InstallationId,
            consumedState.FlowType);
        return new GitHubInstallationCallbackResult(
            consumedState.ProjectId,
            sourceId,
            installation.InstallationId,
            consumedState.FlowType,
            consumedState.ReturnPath,
            true,
            null,
            externalRedirect);
    }

    private async Task<ConsumedGitHubInstallationState> ConsumeValidatedStateAsync(
        string state,
        ValidatedGitHubInstallationState validated,
        CancellationToken cancellationToken)
    {
        var consumed = await _stateService.ConsumeAsync(
            state,
            validated.InitiatingUserId,
            validated.FlowType,
            cancellationToken);
        if (consumed.ProjectId != validated.ProjectId
            || consumed.InitiatingUserId != validated.InitiatingUserId
            || consumed.AccessRequestId != validated.AccessRequestId
            || consumed.PendingInstallationId != validated.PendingInstallationId
            || !string.Equals(consumed.FlowType, validated.FlowType, StringComparison.Ordinal)
            || !string.Equals(consumed.ReturnPath, validated.ReturnPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("GitHub installation state changed unexpectedly during callback validation.");
        }
        return consumed;
    }

    private async Task<GitHubInstallationCallbackResult> FailureAsync(
        ConsumedGitHubInstallationState state,
        string errorCode,
        CancellationToken cancellationToken)
    {
        string? externalRedirect = null;
        if (string.Equals(state.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal)
            && state.AccessRequestId is Guid requestId)
        {
            var resultToken = await _accessRequests.FailAsync(requestId, errorCode, cancellationToken);
            if (!string.IsNullOrWhiteSpace(resultToken))
            {
                externalRedirect = BuildRequestedResultUrl(resultToken, false, errorCode);
            }
        }
        return new GitHubInstallationCallbackResult(
            state.ProjectId,
            null,
            state.PendingInstallationId,
            state.FlowType,
            state.ReturnPath,
            false,
            errorCode,
            externalRedirect);
    }

    private string BuildRequestedResultUrl(string token, bool succeeded, string? errorCode)
    {
        var builder = new UriBuilder(new Uri(_options.FrontendReturnOrigin, "github/access-updated"));
        var query = new List<string>
        {
            $"token={Uri.EscapeDataString(token)}",
            $"status={(succeeded ? "success" : "failed")}",
            $"flowType={Uri.EscapeDataString(GitHubInstallationFlowTypes.Requested)}"
        };
        if (!string.IsNullOrWhiteSpace(errorCode))
        {
            query.Add($"githubError={Uri.EscapeDataString(errorCode)}");
        }
        builder.Query = string.Join("&", query);
        return builder.Uri.AbsoluteUri;
    }

    private static bool IsCancelledOrDenied(string? setupAction) => setupAction is "cancel" or "cancelled" or "denied";
    private static bool IsSupportedSetupAction(string? setupAction) => setupAction is null or "" or "install" or "update";

    private Uri BuildInstallUrl(string state)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, "github.com")
        {
            Path = $"apps/{Uri.EscapeDataString(_options.AppSlug)}/installations/new",
            Query = $"state={Uri.EscapeDataString(state)}"
        };
        return builder.Uri;
    }
}
