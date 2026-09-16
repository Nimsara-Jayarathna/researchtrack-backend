namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IGitHubInstallationTokenProvider
{
    Task<string> GetTokenAsync(long installationId, CancellationToken cancellationToken);
}
