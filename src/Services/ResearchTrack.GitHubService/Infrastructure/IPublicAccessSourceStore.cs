using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IPublicAccessSourceStore
{
    Task<GitHubAvailableRepositoriesResponse> CreateAsync(
        Guid projectId,
        Guid userId,
        GitHubPublicRepository repository,
        DateTime now,
        CancellationToken cancellationToken);

    Task<AvailableRepositoriesPersistenceResult?> GetAvailableAsync(
        Guid sourceId,
        CancellationToken cancellationToken);
}
