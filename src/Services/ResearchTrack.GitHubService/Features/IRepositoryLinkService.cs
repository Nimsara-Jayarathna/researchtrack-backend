using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IRepositoryLinkService
{
    Task<ProjectGitHubRepositoriesResponse> LinkAsync(Guid userId, LinkGitHubRepositoriesRequest request, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> GetProjectAsync(Guid userId, Guid projectId, CancellationToken cancellationToken);
    Task EnsureBelongsToProjectAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> UnlinkAsync(Guid userId, Guid linkedRepositoryId, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> SetEnabledAsync(Guid userId, Guid linkedRepositoryId, bool enabled, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> SelectPrimaryAsync(Guid userId, Guid linkedRepositoryId, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> UpdateDisplayNameAsync(Guid userId, Guid linkedRepositoryId, string? customName, CancellationToken cancellationToken);
    Task<ProjectGitHubRepositoriesResponse> DisconnectSourceAsync(Guid userId, Guid sourceId, CancellationToken cancellationToken);
    Task RequestManualSyncAsync(Guid userId, Guid projectId, Guid linkedRepositoryId, CancellationToken cancellationToken);
}
