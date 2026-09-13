namespace ResearchTrack.GitHubService.Infrastructure;

public interface IGitHubPublicRepositoryProbe
{
    Task<GitHubPublicRepositoryProbeResult> ProbeAsync(
        string owner,
        string repository,
        CancellationToken cancellationToken);
}
