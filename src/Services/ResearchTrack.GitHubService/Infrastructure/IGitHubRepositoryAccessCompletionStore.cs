using ResearchTrack.GitHubService.Infrastructure.GitHubApp;

namespace ResearchTrack.GitHubService.Infrastructure;

public interface IGitHubRepositoryAccessCompletionStore
{
    Task<OwnerGrantedRepositoryCompletionPersistenceResult> CompleteAsync(
        Guid requestId,
        GitHubInstallationInfo installation,
        GitHubInstallationRepository repository,
        DateTime now,
        CancellationToken cancellationToken);
}
