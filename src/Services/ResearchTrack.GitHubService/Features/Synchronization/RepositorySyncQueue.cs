using Prometheus;
using System.Collections.Concurrent;
using System.Threading.Channels;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class RepositorySyncQueue : IRepositorySyncQueue, IInitialRepositorySyncRequester
{
    private readonly ConcurrentDictionary<Guid, byte> _queuedOrRunning = new();
    private readonly Channel<RepositorySyncWorkItem> _channel = Channel.CreateBounded<RepositorySyncWorkItem>(
        new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "github_sync_queue_depth",
        "Number of GitHub repository syncs currently queued or in progress.");

    public async ValueTask EnqueueAsync(
        RepositorySyncWorkItem item,
        CancellationToken cancellationToken)
    {
        if (!_queuedOrRunning.TryAdd(item.LinkedRepositoryId, 0))
        {
            return;
        }

        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken);
            QueueDepth.Set(_queuedOrRunning.Count);
        }
        catch
        {
            _queuedOrRunning.TryRemove(item.LinkedRepositoryId, out _);
            QueueDepth.Set(_queuedOrRunning.Count);
            throw;
        }
    }

    public IAsyncEnumerable<RepositorySyncWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Guid linkedRepositoryId)
    {
        _queuedOrRunning.TryRemove(linkedRepositoryId, out _);
        QueueDepth.Set(_queuedOrRunning.Count);
    }

    public Task RequestAsync(InitialRepositorySyncRequest request, CancellationToken cancellationToken) =>
        EnqueueAsync(
            new RepositorySyncWorkItem(request.LinkedRepositoryId, GitHubSyncTriggers.InitialLink),
            cancellationToken).AsTask();
}