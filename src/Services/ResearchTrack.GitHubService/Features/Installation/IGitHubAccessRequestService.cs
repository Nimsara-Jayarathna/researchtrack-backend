using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public sealed record PendingGitHubAccessRequest(
    Guid Id,
    Guid ProjectId,
    Guid RequestedByUserId,
    string ProjectTitle,
    string OwnerLogin,
    DateTime ExpiresAt);

public interface IGitHubAccessRequestService
{
    Task<GitHubAccessRequestCreateResponse> CreateAsync(Guid userId, Guid projectId, string ownerLogin, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubAccessRequestSummaryResponse>> ListAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task RevokeAsync(Guid userId, Guid projectId, Guid requestId, CancellationToken cancellationToken);
    Task<GitHubAccessRequestValidationResponse> ValidateAsync(string token, CancellationToken cancellationToken);
    Task<PendingGitHubAccessRequest> ResolvePendingAsync(string token, CancellationToken cancellationToken);
    Task EnsureInstallationOwnerAsync(Guid requestId, string ownerLogin, CancellationToken cancellationToken);
    Task<string?> CompleteAsync(Guid requestId, Guid sourceId, long installationId, CancellationToken cancellationToken);
    Task<string?> FailAsync(Guid requestId, string errorCode, CancellationToken cancellationToken);
    Task<GitHubAccessUpdatedSummaryResponse> GetResultAsync(string token, CancellationToken cancellationToken);
    Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeAsync(string token, CancellationToken cancellationToken);
    Task<GitHubAccessUpdatedSummaryResponse> GetLatestCompletedAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task<GitHubAccessUpdatedAcknowledgeResponse> AcknowledgeLatestAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
}
