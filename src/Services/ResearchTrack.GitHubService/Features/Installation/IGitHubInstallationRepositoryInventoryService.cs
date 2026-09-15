using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubInstallationRepositoryInventoryService
{
    Task<GitHubAvailableRepositoriesResponse?> TryRefreshAsync(
        Guid sourceId,
        CancellationToken cancellationToken);
}
