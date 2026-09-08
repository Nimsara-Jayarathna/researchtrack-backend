using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IPublicAccessSourceService
{
    Task<GitHubAvailableRepositoriesResponse> CreateAsync(
        Guid userId,
        CreatePublicAccessSourceRequest request,
        CancellationToken cancellationToken);
}
