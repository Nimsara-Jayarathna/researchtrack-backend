namespace ResearchTrack.GitHubService.Infrastructure.GitHubApp;

public interface IGitHubUserAuthorizationClient
{
    Task<GitHubUserAccessToken> ExchangeCodeAsync(
        string code,
        string codeVerifier,
        CancellationToken cancellationToken);
}
