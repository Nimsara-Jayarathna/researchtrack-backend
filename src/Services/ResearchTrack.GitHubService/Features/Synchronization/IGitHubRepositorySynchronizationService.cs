namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IGitHubRepositorySynchronizationService
{
    Task SynchronizeAsync(
        Guid linkedRepositoryId,
        string trigger,
        CancellationToken cancellationToken);
}
