using System.Text.Json;

namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public interface IGitHubInstallationApiClient
{
    Task<JsonDocument> GetAsync(
        string relativePath,
        GitHubInstallationToken token,
        CancellationToken cancellationToken);
}
