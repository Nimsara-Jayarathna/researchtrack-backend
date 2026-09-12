namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public interface IGitHubAppClient
{
    Task<GitHubInstallationInfo> GetInstallationAsync(
        long installationId,
        CancellationToken cancellationToken);

    Task<GitHubInstallationToken> CreateInstallationTokenAsync(
        long installationId,
        CancellationToken cancellationToken);
}
