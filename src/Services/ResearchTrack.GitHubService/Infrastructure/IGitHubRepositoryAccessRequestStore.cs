using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IGitHubRepositoryAccessRequestStore
{
    Task CreateAsync(GitHubRepositoryAccessRequest request, CancellationToken cancellationToken);
    Task<GitHubRepositoryAccessRequest?> FindByIdAsync(Guid requestId, CancellationToken cancellationToken);
    Task<GitHubRepositoryAccessRequest?> FindByTokenHashAsync(string requestTokenHash, CancellationToken cancellationToken);
    Task<OwnerGrantProjectLinkState> GetProjectLinkStateAsync(
        Guid projectId,
        string normalizedFullName,
        CancellationToken cancellationToken);
    Task<GitHubRepositoryAccessRequest?> TryFailAsync(
        Guid requestId,
        string failureCode,
        DateTime now,
        CancellationToken cancellationToken);
    Task<GitHubRepositoryAccessRequest?> TryExpireAsync(
        Guid requestId,
        DateTime now,
        CancellationToken cancellationToken);
}
