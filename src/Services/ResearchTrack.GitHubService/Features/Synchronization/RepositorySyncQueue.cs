using Prometheus;
using System.Collections.Concurrent;
using System.Threading.Channels;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class RepositorySyncQueue : IRepositorySyncQueue, IInitialRepositorySyncRequester
{
    private readonly ConcurrentDictionary<Guid, SyncQueueState> _states = new();
    private static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "github_sync_queue_depth",
        "Number of GitHub repositories currently queued or running a sync.");

    private readonly Channel<RepositorySyncWorkItem> _channel = Channel.CreateUnbounded<RepositorySyncWorkItem>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    public async ValueTask EnqueueAsync(
        RepositorySyncWorkItem item,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_states.TryGetValue(item.LinkedRepositoryId, out var existing))
            {
                lock (existing.Gate)
                {
                    if (!_states.TryGetValue(item.LinkedRepositoryId, out var current)
                        || !ReferenceEquals(current, existing))
                    {
                        continue;
                    }

                    if (existing.Running)
                    {
                        existing.RerunRequested = true;
                        existing.RerunTrigger = item.Trigger;
                    }
                    return;
                }
            }

            var state = new SyncQueueState();
            if (!_states.TryAdd(item.LinkedRepositoryId, state))
            {
                continue;
            }

            QueueDepth.Set(_states.Count);

            try
            {
                await _channel.Writer.WriteAsync(item, cancellationToken);
                return;
            }
            catch
            {
                _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(item.LinkedRepositoryId, state));
                QueueDepth.Set(_states.Count);
                throw;
            }
        }
    }

    public IAsyncEnumerable<RepositorySyncWorkItem> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void MarkRunning(Guid linkedRepositoryId)
    {
        while (_states.TryGetValue(linkedRepositoryId, out var state))
        {
            lock (state.Gate)
            {
                if (!_states.TryGetValue(linkedRepositoryId, out var current)
                    || !ReferenceEquals(current, state))
                {
                    continue;
                }
                state.Running = true;
                return;
            }
        }
    }

    public void Complete(Guid linkedRepositoryId)
    {
        if (_states.TryGetValue(linkedRepositoryId, out var state))
        {
            lock (state.Gate)
            {
                _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(linkedRepositoryId, state));
            }
            QueueDepth.Set(_states.Count);
        }
    }

    public async ValueTask CompleteAsync(
        Guid linkedRepositoryId,
        CancellationToken cancellationToken)
    {
        RepositorySyncWorkItem? rerun = null;
        SyncQueueState? state = null;
        while (_states.TryGetValue(linkedRepositoryId, out state))
        {
            lock (state.Gate)
            {
                if (!_states.TryGetValue(linkedRepositoryId, out var current)
                    || !ReferenceEquals(current, state))
                {
                    continue;
                }

                if (state.RerunRequested)
                {
                    state.Running = false;
                    state.RerunRequested = false;
                    var trigger = state.RerunTrigger ?? GitHubSyncTriggers.Webhook;
                    state.RerunTrigger = null;
                    rerun = new RepositorySyncWorkItem(linkedRepositoryId, trigger);
                }
                else
                {
                    _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(linkedRepositoryId, state));
                }
                break;
            }
        }

        QueueDepth.Set(_states.Count);

        if (rerun is null)
        {
            return;
        }

        try
        {
            await _channel.Writer.WriteAsync(rerun, cancellationToken);
        }
        catch
        {
            if (state is not null)
            {
                _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(linkedRepositoryId, state));
                QueueDepth.Set(_states.Count);
            }
            throw;
        }
    }

    public Task RequestAsync(InitialRepositorySyncRequest request, CancellationToken cancellationToken) =>
        EnqueueAsync(
            new RepositorySyncWorkItem(request.LinkedRepositoryId, GitHubSyncTriggers.InitialLink),
            cancellationToken).AsTask();

    private sealed class SyncQueueState
    {
        public object Gate { get; } = new();
        public bool Running { get; set; }
        public bool RerunRequested { get; set; }
        public string? RerunTrigger { get; set; }
    }
}