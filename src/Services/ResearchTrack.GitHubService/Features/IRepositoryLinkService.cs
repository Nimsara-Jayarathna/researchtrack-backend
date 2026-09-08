using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IRepositoryLinkService
{
    Task<ProjectGitHubRepositoriesResponse> LinkAsync(
        Guid userId,
        LinkGitHubRepositoriesRequest request,
        CancellationToken cancellationToken);
}
