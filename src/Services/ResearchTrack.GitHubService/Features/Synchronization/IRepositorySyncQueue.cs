namespace ResearchTrack.GitHubService.Features.Synchronization;

public interface IRepositorySyncQueue
{
    ValueTask EnqueueAsync(RepositorySyncWorkItem item, CancellationToken cancellationToken);
    IAsyncEnumerable<RepositorySyncWorkItem> ReadAllAsync(CancellationToken cancellationToken);
    void MarkRunning(Guid linkedRepositoryId) { }
    void Complete(Guid linkedRepositoryId);
    ValueTask CompleteAsync(Guid linkedRepositoryId, CancellationToken cancellationToken)
    {
        Complete(linkedRepositoryId);
        return ValueTask.CompletedTask;
    }
}

public sealed record RepositorySyncWorkItem(Guid LinkedRepositoryId, string Trigger);
