using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IRepositoryLinkStore
{
    Task<RepositoryLinkPersistenceResult> CreateLinksAsync(
        Guid projectId,
        Guid sourceId,
        Guid userId,
        IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
        DateTime now,
        CancellationToken cancellationToken);

    Task MarkSyncFailedAsync(
        Guid linkedRepositoryId,
        DateTime now,
        CancellationToken cancellationToken);

    Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken);
}
