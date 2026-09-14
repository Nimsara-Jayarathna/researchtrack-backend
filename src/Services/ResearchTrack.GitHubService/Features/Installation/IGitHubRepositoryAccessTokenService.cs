using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubRepositoryAccessTokenService
{
    Task<GitHubRepositoryAccessRequestStatusResponse> ValidateAsync(
        string? requestToken,
        CancellationToken cancellationToken);

    Task<GitHubRepositoryAccessRequestStatusResponse> RequirePendingAsync(
        string? requestToken,
        CancellationToken cancellationToken);
}
