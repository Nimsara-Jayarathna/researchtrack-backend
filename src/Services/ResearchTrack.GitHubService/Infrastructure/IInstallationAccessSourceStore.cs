using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IInstallationAccessSourceStore
{
    Task<Guid> CreateAsync(
        Guid projectId,
        Guid userId,
        GitHubInstallationInfo installation,
        DateTime now,
        CancellationToken cancellationToken);
}
