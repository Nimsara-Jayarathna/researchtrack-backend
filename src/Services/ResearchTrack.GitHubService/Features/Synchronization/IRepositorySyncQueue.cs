namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IRepositorySyncQueue
{
    ValueTask EnqueueAsync(RepositorySyncWorkItem item, CancellationToken cancellationToken);
    IAsyncEnumerable<RepositorySyncWorkItem> ReadAllAsync(CancellationToken cancellationToken);
    void Complete(Guid linkedRepositoryId);
}

public sealed record RepositorySyncWorkItem(Guid LinkedRepositoryId, string Trigger);
