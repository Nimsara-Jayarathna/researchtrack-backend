namespace ResearchTrack.GitHubService.Infrastructure;

public interface IProjectMetadataClient
{
    Task<string> GetTitleAsync(Guid projectId, CancellationToken cancellationToken);
}
