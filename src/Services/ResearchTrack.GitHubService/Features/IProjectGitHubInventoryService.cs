using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IProjectGitHubInventoryService
{
    Task<ProjectGitHubRepositoryListingResponse> GetAsync(
        Guid userId,
        Guid projectId,
        CancellationToken cancellationToken);
}
