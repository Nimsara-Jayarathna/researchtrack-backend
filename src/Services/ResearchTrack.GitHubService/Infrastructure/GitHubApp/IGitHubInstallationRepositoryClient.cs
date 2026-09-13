namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public interface IGitHubInstallationRepositoryClient
{
    Task<GitHubInstallationRepositoryPage> ListAsync(
        GitHubInstallationToken token,
        int page,
        int perPage,
        CancellationToken cancellationToken);

    Task<GitHubInstallationRepository> GetAsync(
        GitHubInstallationToken token,
        long repositoryId,
        CancellationToken cancellationToken);
}
