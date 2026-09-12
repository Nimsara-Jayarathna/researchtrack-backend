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
    private readonly IGitHubUserAuthorizationClient _gitHubUserAuthorizationClient;
    private readonly IGitHubUserInstallationClient _gitHubUserInstallationClient;
    private readonly GitHubOAuthPkce _oauthPkce;
    private readonly IInstallationAccessSourceStore _accessSourceStore;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubInstallationFlowService> _logger;

    public GitHubInstallationFlowService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubInstallationStateService stateService,
        IGitHubAppClient gitHubAppClient,
        IGitHubUserAuthorizationClient gitHubUserAuthorizationClient,
        IGitHubUserInstallationClient gitHubUserInstallationClient,
        GitHubOAuthPkce oauthPkce,
        IInstallationAccessSourceStore accessSourceStore,
        GitHubAppOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubInstallationFlowService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _stateService = stateService;
        _gitHubAppClient = gitHubAppClient;
        _gitHubUserAuthorizationClient = gitHubUserAuthorizationClient;
        _gitHubUserInstallationClient = gitHubUserInstallationClient;
        _oauthPkce = oauthPkce;
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
                new ApiFieldError("requestToken", ["Requested-access installation is handled by its existing request flow."])
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

        var returnPath = $"/supervisor/projects/{projectId:D}";
        var state = await _stateService.CreateAsync(
            projectId,
            userId,
            GitHubInstallationFlowTypes.Direct,
            returnPath,
            cancellationToken);

        var installUrl = BuildInstallUrl(state.Value);
        _logger.LogInformation(
            "GitHub App installation flow started. ProjectId={ProjectId} UserId={UserId} FlowType={FlowType}",
            projectId,
            userId,
            GitHubInstallationFlowTypes.Direct);

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
        var normalizedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
        var normalizedError = string.IsNullOrWhiteSpace(error) ? null : error.Trim().ToLowerInvariant();

        // This callback is intentionally anonymous because the ResearchTrack auth cookie is SameSite=Strict
        // and is not guaranteed to survive a github.com -> ResearchTrack redirect. The short-lived, one-time
        // server-side state is therefore the authenticated callback context. No project/user/return URL is
        // accepted from the browser here.
        var validated = await _stateService.ValidateExternalCallbackAsync(
            normalizedState,
            GitHubInstallationFlowTypes.Direct,
            cancellationToken);
        var initiatingUserId = validated.InitiatingUserId;

        if (normalizedError is not null)
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            var errorCode = normalizedError == "access_denied"
                ? "cancelled"
                : "github_authorization_failed";

            _logger.LogInformation(
                "GitHub user authorization did not complete. ProjectId={ProjectId} UserId={UserId} ErrorCode={ErrorCode}",
                consumed.ProjectId,
                consumed.InitiatingUserId,
                errorCode);

            return Failure(consumed, errorCode);
        }

        // Second leg: GitHub user authorization callback. The pending installation was bound atomically during
        // the setup callback, then the user token proves the GitHub user can actually access that installation.
        if (normalizedCode is not null)
        {
            if (validated.PendingInstallationId is not long pendingInstallationId || pendingInstallationId <= 0)
            {
                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                return Failure(consumed, "invalid_callback");
            }

            try
            {
                var verifier = _oauthPkce.CreateVerifier(normalizedState);
                var userToken = await _gitHubUserAuthorizationClient.ExchangeCodeAsync(
                    normalizedCode,
                    verifier,
                    cancellationToken);
                var userCanAccessInstallation = await _gitHubUserInstallationClient.CanAccessInstallationAsync(
                    userToken,
                    pendingInstallationId,
                    cancellationToken);

                if (!userCanAccessInstallation)
                {
                    var consumedMismatch = await ConsumeValidatedStateAsync(
                        normalizedState,
                        validated,
                        cancellationToken);
                    _logger.LogWarning(
                        "GitHub user authorization could not validate the pending installation. ProjectId={ProjectId} InstallationId={InstallationId}",
                        consumedMismatch.ProjectId,
                        pendingInstallationId);
                    return Failure(consumedMismatch, "installation_identity_mismatch");
                }

                // Fetch canonical account metadata with the App identity after the user/installation relationship
                // has been proven. installation_id and owner metadata are never trusted from browser parameters.
                var installation = await _gitHubAppClient.GetInstallationAsync(
                    pendingInstallationId,
                    cancellationToken);

                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                if (consumed.PendingInstallationId != pendingInstallationId)
                {
                    throw new InvalidOperationException(
                        "GitHub installation state changed unexpectedly during identity verification.");
                }

                var sourceId = await _accessSourceStore.CreateAsync(
                    consumed.ProjectId,
                    initiatingUserId,
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
            catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status400BadRequest)
            {
                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                return Failure(consumed, "github_authorization_failed");
            }
            catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
            {
                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                return Failure(consumed, "invalid_installation");
            }
            catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status403Forbidden)
            {
                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                return Failure(consumed, "installation_identity_mismatch");
            }
            catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                var consumed = await ConsumeValidatedStateAsync(
                    normalizedState,
                    validated,
                    cancellationToken);
                _logger.LogWarning(
                    "GitHub installation identity verification was unavailable. ProjectId={ProjectId} InstallationId={InstallationId}",
                    consumed.ProjectId,
                    pendingInstallationId);
                return Failure(consumed, "github_unavailable");
            }
        }

        // First leg: GitHub App setup callback. Validate ResearchTrack state before looking at installation_id.
        if (IsCancelledOrDenied(normalizedAction))
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            _logger.LogInformation(
                "GitHub App installation did not complete. ProjectId={ProjectId} UserId={UserId} SetupAction={SetupAction}",
                consumed.ProjectId,
                consumed.InitiatingUserId,
                normalizedAction ?? "missing");
            return Failure(consumed, "cancelled");
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
            _logger.LogWarning(
                "GitHub App callback used an unsupported setup action. ProjectId={ProjectId} SetupAction={SetupAction}",
                consumed.ProjectId,
                normalizedAction);
            return Failure(consumed, "invalid_setup_action");
        }

        try
        {
            // App-level verification proves the ID is a real installation for this App. It is deliberately
            // insufficient for persistence; the following user-authorization leg binds it to the GitHub user.
            await _gitHubAppClient.GetInstallationAsync(installationId.Value, cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == StatusCodes.Status404NotFound)
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            _logger.LogWarning(
                "GitHub App callback referenced an inaccessible installation. ProjectId={ProjectId} InstallationId={InstallationId}",
                consumed.ProjectId,
                installationId.Value);
            return Failure(consumed, "invalid_installation");
        }
        catch (ApiException exception) when (exception.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            var consumed = await ConsumeValidatedStateAsync(
                normalizedState,
                validated,
                cancellationToken);
            return Failure(consumed, "github_unavailable");
        }

        var bound = await _stateService.BindInstallationAsync(
            normalizedState,
            installationId.Value,
            GitHubInstallationFlowTypes.Direct,
            cancellationToken);
        EnsureSameStateContext(validated, bound);

        _logger.LogInformation(
            "GitHub App installation validated and awaiting GitHub user authorization. ProjectId={ProjectId} InstallationId={InstallationId}",
            bound.ProjectId,
            installationId.Value);

        return new GitHubInstallationCallbackResult(
            bound.ProjectId,
            null,
            installationId.Value,
            bound.FlowType,
            bound.ReturnPath,
            false,
            null,
            BuildUserAuthorizationUrl(normalizedState).AbsoluteUri);
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
        EnsureSameStateContext(validated, consumed, allowInstallationBinding: true);
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

    private static void EnsureSameStateContext(
        ValidatedGitHubInstallationState expected,
        ValidatedGitHubInstallationState actual)
    {
        if (actual.ProjectId != expected.ProjectId
            || actual.InitiatingUserId != expected.InitiatingUserId
            || !string.Equals(actual.FlowType, expected.FlowType, StringComparison.Ordinal)
            || !string.Equals(actual.ReturnPath, expected.ReturnPath, StringComparison.Ordinal)
            || (expected.PendingInstallationId is long expectedInstallation
                && actual.PendingInstallationId != expectedInstallation))
        {
            throw new InvalidOperationException(
                "GitHub installation state changed unexpectedly during callback validation.");
        }
    }

    private static void EnsureSameStateContext(
        ValidatedGitHubInstallationState validated,
        ConsumedGitHubInstallationState consumed,
        bool allowInstallationBinding = false)
    {
        if (consumed.ProjectId != validated.ProjectId
            || consumed.InitiatingUserId != validated.InitiatingUserId
            || !string.Equals(consumed.FlowType, validated.FlowType, StringComparison.Ordinal)
            || !string.Equals(consumed.ReturnPath, validated.ReturnPath, StringComparison.Ordinal)
            || (!allowInstallationBinding && consumed.PendingInstallationId != validated.PendingInstallationId)
            || (validated.PendingInstallationId is long expectedInstallation
                && consumed.PendingInstallationId != expectedInstallation))
        {
            throw new InvalidOperationException(
                "GitHub installation state changed unexpectedly during callback validation.");
        }
    }

    private Uri BuildInstallUrl(string state)
    {
        var builder = new UriBuilder(Uri.UriSchemeHttps, "github.com")
        {
            Path = $"apps/{Uri.EscapeDataString(_options.AppSlug)}/installations/new",
            Query = $"state={Uri.EscapeDataString(state)}"
        };
        return builder.Uri;
    }

    private Uri BuildUserAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.SetupCallbackUrl.AbsoluteUri,
            ["state"] = state,
            ["code_challenge"] = _oauthPkce.CreateChallenge(state),
            ["code_challenge_method"] = "S256"
        };

        var builder = new UriBuilder(Uri.UriSchemeHttps, "github.com")
        {
            Path = "login/oauth/authorize",
            Query = string.Join("&", query.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"))
        };
        return builder.Uri;
    }
}
