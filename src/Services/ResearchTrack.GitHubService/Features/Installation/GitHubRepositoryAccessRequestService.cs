using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Configuration;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubRepositoryAccessRequestService : IGitHubRepositoryAccessRequestService
{
    private readonly IProjectAuthorizationClient _projectAuthorization;
    private readonly IGitHubRepositoryAccessRequestStore _store;
    private readonly GitHubRepositoryLinkOptions _linkOptions;
    private readonly GitHubAppOptions _appOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubRepositoryAccessRequestService> _logger;

    public GitHubRepositoryAccessRequestService(
        IProjectAuthorizationClient projectAuthorization,
        IGitHubRepositoryAccessRequestStore store,
        GitHubRepositoryLinkOptions linkOptions,
        GitHubAppOptions appOptions,
        TimeProvider timeProvider,
        ILogger<GitHubRepositoryAccessRequestService> logger)
    {
        _projectAuthorization = projectAuthorization;
        _store = store;
        _linkOptions = linkOptions;
        _appOptions = appOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubRepositoryAccessRequestCreateResponse> CreateAsync(
        Guid userId,
        CreateGitHubRepositoryAccessRequest request,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
        }
        if (request.ProjectId == Guid.Empty)
        {
            throw new ApiValidationException([
                new ResearchTrack.BuildingBlocks.Api.Contracts.ApiFieldError("projectId", ["Project id is required."])
            ]);
        }

        await _projectAuthorization.EnsureCanManageAsync(request.ProjectId, cancellationToken);

        var parsed = ResearchTrack.GitHubService.Features.GitHubRepositoryUrlParser.Parse(request.RepositoryUrl);
        var owner = parsed.Owner.ToLowerInvariant();
        var repositoryName = parsed.Repository.ToLowerInvariant();
        var fullName = $"{owner}/{repositoryName}";

        // This is only an early policy check. Exact GitHub identity and all limits are re-checked
        // transactionally at completion because project links can change while the request is pending.
        var linkState = await _store.GetProjectLinkStateAsync(request.ProjectId, fullName, cancellationToken);
        if (linkState.ExactRepositoryAlreadyLinked)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The requested GitHub repository is already linked to this project.");
        }
        if (linkState.ActiveLinkedRepositories >= _linkOptions.MaxLinkedRepositories)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The project has reached its linked GitHub repository limit.");
        }
        if (linkState.ActiveEnabledRepositories >= _linkOptions.MaxEnabledRepositories)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The project has reached its enabled GitHub repository limit.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var rawToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        var accessRequest = new GitHubRepositoryAccessRequest
        {
            Id = Guid.NewGuid(),
            ProjectId = request.ProjectId,
            InitiatingUserId = userId,
            RequestedOwner = owner,
            RequestedRepositoryName = repositoryName,
            RequestedFullName = fullName,
            GitHubRepositoryId = null,
            RequestTokenHash = tokenHash,
            FlowType = GitHubInstallationFlowTypes.Requested,
            Status = GitHubRepositoryAccessRequestStatuses.Pending,
            CreatedAt = now,
            ExpiresAt = now.Add(_appOptions.StateLifetime),
            PendingInstallationId = null,
            AuthorizationStartedAt = null,
            CompletedAt = null,
            ConsumedAt = null,
            FailureCode = null,
            Version = 0
        };

        await _store.CreateAsync(accessRequest, cancellationToken);

        var requestUrl = BuildRequestUrl(rawToken);
        _logger.LogInformation(
            "Owner-granted GitHub request created. RequestId={RequestId} ProjectId={ProjectId} UserId={UserId} ExpiresAt={ExpiresAt}",
            accessRequest.Id,
            accessRequest.ProjectId,
            userId,
            accessRequest.ExpiresAt);

        return new GitHubRepositoryAccessRequestCreateResponse(
            accessRequest.Id,
            accessRequest.ProjectId,
            accessRequest.RequestedOwner,
            accessRequest.RequestedRepositoryName,
            accessRequest.RequestedFullName,
            $"https://github.com/{owner}/{repositoryName}",
            accessRequest.Status,
            accessRequest.ExpiresAt,
            requestUrl.AbsoluteUri);
    }

    public async Task<GitHubRepositoryAccessRequestMemberStatusResponse> GetStatusAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, ErrorCodes.Unauthorized, "Authentication is required.");
        }
        if (requestId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Owner-granted GitHub request was not found.");
        }

        var request = await _store.FindByIdAsync(requestId, cancellationToken)
            ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Owner-granted GitHub request was not found.");

        await _projectAuthorization.EnsureCanManageAsync(request.ProjectId, cancellationToken);
        if (request.InitiatingUserId != userId)
        {
            // Do not disclose lifecycle/repository data to a different project user.
            throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Owner-granted GitHub request was not found.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal)
            && request.ExpiresAt <= now)
        {
            request = await _store.TryExpireAsync(request.Id, now, cancellationToken)
                ?? await _store.FindByIdAsync(request.Id, cancellationToken)
                ?? throw new ApiException(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "Owner-granted GitHub request was not found.");
        }

        return ToMemberResponse(request);
    }

    private Uri BuildRequestUrl(string rawToken)
    {
        var baseUri = new Uri(_appOptions.FrontendReturnOrigin, "/github/request-access");
        var builder = new UriBuilder(baseUri)
        {
            Query = $"token={Uri.EscapeDataString(rawToken)}"
        };
        return builder.Uri;
    }

    private static GitHubRepositoryAccessRequestMemberStatusResponse ToMemberResponse(
        GitHubRepositoryAccessRequest request) => new(
        request.Id,
        request.ProjectId,
        request.RequestedOwner,
        request.RequestedRepositoryName,
        request.RequestedFullName,
        $"https://github.com/{request.RequestedOwner}/{request.RequestedRepositoryName}",
        request.Status,
        DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Utc),
        GitHubRepositoryAccessFailureCodes.IsSafe(request.FailureCode) ? request.FailureCode : null);
}
