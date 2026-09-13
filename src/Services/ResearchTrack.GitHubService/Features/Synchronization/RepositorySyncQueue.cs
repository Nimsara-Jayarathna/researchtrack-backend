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
        }
        catch
        {
            _queuedOrRunning.TryRemove(item.LinkedRepositoryId, out _);
            throw;
        }
    }

    public IAsyncEnumerable<RepositorySyncWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete(Guid linkedRepositoryId) =>
        _queuedOrRunning.TryRemove(linkedRepositoryId, out _);

    public Task RequestAsync(InitialRepositorySyncRequest request, CancellationToken cancellationToken) =>
        EnqueueAsync(
            new RepositorySyncWorkItem(request.LinkedRepositoryId, GitHubSyncTriggers.InitialLink),
            cancellationToken).AsTask();
}
