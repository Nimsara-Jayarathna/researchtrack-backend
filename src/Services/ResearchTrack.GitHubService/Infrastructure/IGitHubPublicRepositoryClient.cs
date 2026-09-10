namespace ResearchTrack.GitHubService.Infrastructure;

public interface IGitHubPublicRepositoryClient
{
    Task<GitHubPublicRepository> GetAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken);
}
