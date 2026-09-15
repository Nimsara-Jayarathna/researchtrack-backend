using System.Collections.Concurrent;
using System.Threading.Channels;
using ResearchTrack.GitHubService.Domain;

namespace ResearchTrack.GitHubService.Features.Synchronization;

public sealed class RepositorySyncQueue : IRepositorySyncQueue, IInitialRepositorySyncRequester
{
    private readonly ConcurrentDictionary<Guid, SyncQueueState> _states = new();
    // The reader calls CompleteAsync after each synchronization and may need to
    // enqueue one coalesced rerun. An unbounded channel avoids a bounded-channel
    // self-deadlock where the single reader could otherwise block trying to write
    // that rerun while no reader is available to free capacity. Per-repository
    // state still coalesces duplicate requests, so one noisy repository cannot
    // grow the queue without bound.
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

                    // A queued item has not started reading GitHub yet, so it will
                    // naturally observe the newest state. An event arriving while
                    // the sync is already running must request one more pass after
                    // completion or the newer GitHub state can be lost.
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

            try
            {
                await _channel.Writer.WriteAsync(item, cancellationToken);
                return;
            }
            catch
            {
                _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(item.LinkedRepositoryId, state));
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
        // Kept for compatibility with test doubles and older call sites. The
        // production worker uses CompleteAsync so a requested rerun can be
        // written back to the bounded channel safely.
        if (_states.TryGetValue(linkedRepositoryId, out var state))
        {
            lock (state.Gate)
            {
                _states.TryRemove(new KeyValuePair<Guid, SyncQueueState>(linkedRepositoryId, state));
            }
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
