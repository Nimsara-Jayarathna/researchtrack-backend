using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubRepositoryAccessContinuationService
{
    Task<GitHubRepositoryAccessRequestContinueResponse> ContinueAsync(
        string? requestToken,
        CancellationToken cancellationToken);
}
