using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Features.Installation;

public interface IGitHubInstallationRepositoryService
{
    Task<GitHubAvailableRepositoriesResponse?> TryGetAvailableAsync(
        Guid userId,
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<GitHubInstallationRepositoriesPageResponse> GetInstallationPageAsync(
        Guid userId,
        Guid projectId,
        long installationId,
        int page,
        int size,
        CancellationToken cancellationToken);

    Task<LegacyInstallationRepositorySelection> ResolveLegacySelectionAsync(
        Guid userId,
        Guid projectId,
        long installationId,
        long repositoryId,
        CancellationToken cancellationToken);

    Task<bool> TryVerifyForLinkAsync(
        Guid userId,
        Guid projectId,
        Guid sourceId,
        IReadOnlyList<LinkGitHubRepositoryRequestItem> repositories,
        CancellationToken cancellationToken);

    Task<GitHubInstallationRepository> VerifyRequestedRepositoryAsync(
        long installationId,
        string requestedOwner,
        string requestedRepositoryName,
        CancellationToken cancellationToken);
}
