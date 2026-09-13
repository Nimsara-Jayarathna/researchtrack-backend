using ResearchTrack.GitHubService.Contracts;
using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IInstallationRepositoryStore
{
    Task<InstallationAccessSourceSnapshot?> GetSourceAsync(
        Guid sourceId,
        CancellationToken cancellationToken);

    Task<InstallationAccessSourceSnapshot?> GetSourceByInstallationAsync(
        Guid projectId,
        long installationId,
        CancellationToken cancellationToken);

    Task<GitHubAvailableRepositoriesResponse> UpsertAvailableAsync(
        Guid sourceId,
        IReadOnlyList<GitHubInstallationRepository> repositories,
        DateTime now,
        CancellationToken cancellationToken);

    Task<InstallationRepositorySelection?> GetSelectionAsync(
        Guid sourceId,
        Guid repositoryId,
        CancellationToken cancellationToken);

    Task<Guid> UpsertVerifiedAsync(
        Guid sourceId,
        GitHubInstallationRepository repository,
        DateTime now,
        CancellationToken cancellationToken);
}
