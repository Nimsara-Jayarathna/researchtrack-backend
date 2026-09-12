namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public interface IGitHubUserInstallationClient
{
    Task<bool> CanAccessInstallationAsync(
        GitHubUserAccessToken token,
        long installationId,
        CancellationToken cancellationToken);
}
