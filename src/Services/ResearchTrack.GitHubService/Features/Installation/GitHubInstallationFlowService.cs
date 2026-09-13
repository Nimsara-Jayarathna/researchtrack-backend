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
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationFlowService> _logger;

    public GitHubInstallationFlowService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubInstallationStateService stateService,
        IGitHubAppClient gitHubAppClient,
        IInstallationAccessSourceStore accessSourceStore,
        GitHubAppOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationFlowService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _stateService = stateService;
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

        var installUrl = BuildInstallUrl(state.Value);
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
        var normalizedAction = setupAction?.Trim().ToLowerInvariant();
        var normalizedError = string.IsNullOrWhiteSpace(error)
            ? null
            : error.Trim().ToLowerInvariant();

        // The callback is anonymous because the application auth cookie can be SameSite=Strict.
        // Security is provided by the one-time server-side state generated for the initiating
        // supervisor/project. The installation id is then verified using the GitHub App identity.
        var validated = await _stateService.ValidateExternalCallbackAsync(
            normalizedState,
            GitHubInstallationFlowTypes.Direct,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(code))
        {
            // ResearchTrack uses installation access tokens, not a user OAuth token, for repository
            // synchronization. An OAuth callback here means an obsolete GitHub App configuration is
            // still trying to force user authorization during installation.
            var consumedLegacy = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            _logger.LogWarning(
                "Unexpected GitHub user OAuth callback received. ProjectId={ProjectId}",
                consumedLegacy.ProjectId);
            return Failure(consumedLegacy, "unexpected_user_oauth");
        }

        if (normalizedError is not null || IsCancelledOrDenied(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            var errorCode = normalizedError == "access_denied"
                || IsCancelledOrDenied(normalizedAction)
                    ? "cancelled"
                    : "github_authorization_failed";
            return Failure(consumed, errorCode);
        }

        if (installationId is null || installationId <= 0)
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            return Failure(consumed, "missing_installation");
        }

        if (!IsSupportedSetupAction(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            return Failure(consumed, "invalid_setup_action");
        }

        try
        {
            // This verifies that the installation exists for this GitHub App and supplies canonical
            // owner metadata. Browser-provided owner/account data is never trusted.
            var installation = await _gitHubAppClient.GetInstallationAsync(
                installationId.Value,
                cancellationToken);

            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);

            var sourceId = await _accessSourceStore.CreateAsync(
                consumed.ProjectId,
                consumed.InitiatingUserId,
                installation,
                _timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);

            _logger.LogInformation(
                "GitHub App installation connected. ProjectId={ProjectId} SourceId={SourceId} InstallationId={InstallationId}",
                consumed.ProjectId,
                sourceId,
                installation.InstallationId);

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
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            return Failure(consumed, "invalid_installation");
        }
        catch (ApiException exception) when (
            exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            return Failure(consumed, "github_unavailable");
        }
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

        if (consumed.ProjectId != validated.ProjectId
            || consumed.InitiatingUserId != validated.InitiatingUserId
            || !string.Equals(consumed.FlowType, validated.FlowType, StringComparison.Ordinal)
            || !string.Equals(consumed.ReturnPath, validated.ReturnPath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "GitHub installation state changed unexpectedly during callback validation.");
        }

        return consumed;
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

    private static bool IsCancelledOrDenied(string? setupAction) =>
        setupAction is "cancel" or "cancelled" or "denied";

    private static bool IsSupportedSetupAction(string? setupAction) =>
        setupAction is null or "" or "install" or "update";

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
