namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IGitHubRepositorySynchronizationService
{
    Task<GitHubSynchronizationOutcome> SynchronizeAsync(
        Guid linkedRepositoryId,
        string trigger,
        CancellationToken cancellationToken);
}
