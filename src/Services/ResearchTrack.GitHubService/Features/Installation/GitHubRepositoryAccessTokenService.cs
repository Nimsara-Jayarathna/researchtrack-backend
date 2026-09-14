using System.Security.Cryptography;
using System.Text;
using ResearchTrack.BuildingBlocks.Api.Constants;
using ResearchTrack.BuildingBlocks.Api.Exceptions;
using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Infrastructure;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed class GitHubRepositoryAccessTokenService : IGitHubRepositoryAccessTokenService
{
    private const int MaxTokenLength = 512;
    private readonly IGitHubRepositoryAccessRequestStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GitHubRepositoryAccessTokenService> _logger;

    public GitHubRepositoryAccessTokenService(
        IGitHubRepositoryAccessRequestStore store,
        TimeProvider timeProvider,
        ILogger<GitHubRepositoryAccessTokenService> logger)
    {
        _store = store;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<GitHubRepositoryAccessRequestStatusResponse> ValidateAsync(
        string? requestToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashTokenOrReject(requestToken);
        var request = await _store.FindByTokenHashAsync(tokenHash, cancellationToken)
            ?? throw TokenUnavailable();

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal)
            && request.ExpiresAt <= now)
        {
            request = await _store.TryExpireAsync(request.Id, now, cancellationToken)
                ?? await _store.FindByIdAsync(request.Id, cancellationToken)
                ?? throw TokenUnavailable();

            if (string.Equals(request.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Owner-granted GitHub request expired during token validation. RequestId={RequestId} ProjectId={ProjectId}",
                    request.Id,
                    request.ProjectId);
            }
        }

        return ToSafeResponse(request);
    }

    public async Task<GitHubRepositoryAccessRequestStatusResponse> RequirePendingAsync(
        string? requestToken,
        CancellationToken cancellationToken)
    {
        var response = await ValidateAsync(requestToken, cancellationToken);
        if (string.Equals(response.Status, GitHubRepositoryAccessRequestStatuses.Pending, StringComparison.Ordinal))
        {
            return response;
        }

        var message = string.Equals(response.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal)
            ? "Owner-granted GitHub request has expired."
            : "Owner-granted GitHub request is no longer pending.";
        var statusCode = string.Equals(response.Status, GitHubRepositoryAccessRequestStatuses.Expired, StringComparison.Ordinal)
            ? StatusCodes.Status410Gone
            : StatusCodes.Status409Conflict;
        throw new ApiException(statusCode, ErrorCodes.Conflict, message);
    }

    internal static string HashTokenOrReject(string? requestToken)
    {
        // Do not include the supplied token in validation errors or logs. Malformed and unknown
        // values deliberately converge on the same externally visible unavailable result.
        var token = requestToken?.Trim();
        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength)
        {
            throw TokenUnavailable();
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    }

    private static GitHubRepositoryAccessRequestStatusResponse ToSafeResponse(
        GitHubRepositoryAccessRequest request)
    {
        var safeFailure = GitHubRepositoryAccessFailureCodes.IsSafe(request.FailureCode)
            ? request.FailureCode
            : null;

        return new GitHubRepositoryAccessRequestStatusResponse(
            request.Id,
            request.RequestedOwner,
            request.RequestedRepositoryName,
            request.RequestedFullName,
            $"https://github.com/{request.RequestedOwner}/{request.RequestedRepositoryName}",
            request.Status,
            DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Utc),
            safeFailure);
    }

    private static ApiException TokenUnavailable() => new(
        StatusCodes.Status404NotFound,
        ErrorCodes.NotFound,
        "Owner-granted GitHub request is unavailable.");
}
