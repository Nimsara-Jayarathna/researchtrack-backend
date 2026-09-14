using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubRepositoryAccessContinuationService : IGitHubRepositoryAccessContinuationService
{
    private readonly IGitHubRepositoryAccessTokenService _tokenService;
    private readonly IGitHubRepositoryAccessRequestStore _requestStore;
    private readonly IGitHubInstallationStateService _stateService;
    private readonly GitHubAppOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubRepositoryAccessContinuationService> _logger;

    public GitHubRepositoryAccessContinuationService(
        IGitHubRepositoryAccessTokenService tokenService,
        IGitHubRepositoryAccessRequestStore requestStore,
        IGitHubInstallationStateService stateService,
        GitHubAppOptions options,
        TimeProvider timeProvider,
        ILogger<GitHubRepositoryAccessContinuationService> logger)
    {
        _tokenService = tokenService;
        _requestStore = requestStore;
        _stateService = stateService;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubRepositoryAccessRequestContinueResponse> ContinueAsync(
        string? requestToken,
        CancellationToken cancellationToken)
    {
        var safe = await _tokenService.RequirePendingAsync(requestToken, cancellationToken);
        var request = await _requestStore.FindByIdAsync(safe.RequestId, cancellationToken)
            ?? throw Unavailable();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!string.Equals(request.FlowType, GitHubInstallationFlowTypes.Requested, StringComparison.Ordinal))
        {
            throw Unavailable();
        }
        if (!string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            throw Terminal(request.Status);
        }
        if (request.ExpiresAt <= now)
        {
            var expired = await _requestStore.TryExpireAsync(request.Id, now, cancellationToken)
                ?? await _requestStore.FindByIdAsync(request.Id, cancellationToken);
            if (expired is not null
                && string.Equals(expired.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal))
            {
                throw new ApiException(
                    StatusCodes.Status410Gone,
                    ErrorCodes.Conflict,
                    "Owner-granted GitHub request has expired.");
            }

            throw Terminal(expired?.Status);
        }

        var state = await _stateService.CreateRequestedAsync(
            request.ProjectId,
            request.InitiatingUserId,
            request.Id,
            "/github/access-updated",
            cancellationToken);
        var authorizeUrl = GitHubInstallationUrlBuilder.Build(_options.AppSlug, state.Value);

        _logger.LogInformation(
            "Owner-granted GitHub authorization started. RequestId={RequestId} ProjectId={ProjectId}",
            request.Id,
            request.ProjectId);

        return new GitHubRepositoryAccessRequestContinueResponse(
            request.Id,
            authorizeUrl.AbsoluteUri,
            DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Utc));
    }

    private static ApiException Unavailable() => new(
        StatusCodes.Status404NotFound,
        ErrorCodes.NotFound,
        "Owner-granted GitHub request is unavailable.");

    private static ApiException Terminal(string? status) =>
        string.Equals(status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal)
            ? new ApiException(
                StatusCodes.Status410Gone,
                ErrorCodes.Conflict,
                "Owner-granted GitHub request has expired.")
            : new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Owner-granted GitHub request is no longer pending.");
}
