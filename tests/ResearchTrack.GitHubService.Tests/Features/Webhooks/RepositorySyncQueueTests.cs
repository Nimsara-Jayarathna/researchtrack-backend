using ResearchTrack.GitHubService.Domain;
using ResearchTrack.GitHubService.Features.Synchronization;

namespace ResearchTrack.GitHubService.Tests.Features.Webhooks;

public sealed class RepositorySyncQueueTests
{
    [Fact]
    public async Task EventArrivingWhileRunning_IsCoalescedIntoOneRerun()
    {
        var queue = new RepositorySyncQueue();
        var repositoryId = Guid.NewGuid();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var reader = queue.ReadAllAsync(timeout.Token).GetAsyncEnumerator(timeout.Token);

        await queue.EnqueueAsync(new RepositorySyncWorkItem(repositoryId, GitHubSyncTriggers.Manual), timeout.Token);
        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(GitHubSyncTriggers.Manual, reader.Current.Trigger);
        queue.MarkRunning(repositoryId);

        await queue.EnqueueAsync(new RepositorySyncWorkItem(repositoryId, GitHubSyncTriggers.Webhook), timeout.Token);
        await queue.EnqueueAsync(new RepositorySyncWorkItem(repositoryId, GitHubSyncTriggers.Webhook), timeout.Token);
        await queue.CompleteAsync(repositoryId, timeout.Token);

        Assert.True(await reader.MoveNextAsync());
        Assert.Equal(repositoryId, reader.Current.LinkedRepositoryId);
        Assert.Equal(GitHubSyncTriggers.Webhook, reader.Current.Trigger);
        queue.MarkRunning(repositoryId);
        await queue.CompleteAsync(repositoryId, timeout.Token);
    }
}
