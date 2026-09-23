using ResearchTrack.GitHubService.Contracts;

namespace ResearchTrack.GitHubService.Features;

public interface IGitHubSyncStateQueryService
{
    Task<GitHubSyncStateResponse> GetAsync(Guid projectId, CancellationToken cancellationToken);
}
