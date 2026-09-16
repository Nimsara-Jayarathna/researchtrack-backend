using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IRepositoryLinkStore
{
    Task<RepositoryLinkPersistenceResult> CreateLinksAsync(Guid projectId, Guid sourceId, Guid userId, IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories, DateTime now, CancellationToken cancellationToken);
    Task MarkSyncFailedAsync(Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(Guid projectId, CancellationToken cancellationToken);
    Task<Guid?> GetLinkProjectIdAsync(Guid linkedRepositoryId, CancellationToken cancellationToken);
    Task<Guid?> GetSourceProjectIdAsync(Guid sourceId, CancellationToken cancellationToken);
    Task<RepositoryEnablementPersistenceResult> SetEnabledAsync(Guid linkedRepositoryId, bool enabled, DateTime now, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> UnlinkAsync(Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> SelectPrimaryAsync(Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> UpdateDisplayNameAsync(Guid linkedRepositoryId, string? customName, DateTime now, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> DisconnectSourceAsync(Guid sourceId, DateTime now, CancellationToken cancellationToken);
    Task PrepareManualSyncAsync(Guid projectId, Guid linkedRepositoryId, DateTime now, CancellationToken cancellationToken);
}
