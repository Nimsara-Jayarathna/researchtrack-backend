using System.Threading.Channels;

namespace ResearchTrack.BuildingBlocks.Api.RuntimeLogging;

public sealed class RuntimeLogStore
{
    public const int Capacity = 1000;

    private readonly Lock _lock = new();
    private readonly Queue<RuntimeLogEntry> _entries = new(Capacity);
    private readonly Dictionary<Guid, Channel<RuntimeLogEntry>> _subscribers = [];
    private long _nextId;

    public RuntimeLogEntry Add(RuntimeLogEntry entry)
    {
        RuntimeLogEntry storedEntry;
        Channel<RuntimeLogEntry>[] subscribers;

        lock (_lock)
        {
            storedEntry = entry with { Id = ++_nextId };
            if (_entries.Count == Capacity)
            {
                _entries.Dequeue();
            }

            _entries.Enqueue(storedEntry);
            subscribers = [.. _subscribers.Values];
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(storedEntry);
        }

        return storedEntry;
    }

    public IReadOnlyList<RuntimeLogEntry> GetLatest(int limit)
    {
        var safeLimit = Math.Clamp(limit, 1, Capacity);
        lock (_lock)
        {
            return _entries.TakeLast(safeLimit).ToArray();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
    }

    public RuntimeLogSubscription Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<RuntimeLogEntry>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_lock)
        {
            _subscribers.Add(id, channel);
        }

        return new RuntimeLogSubscription(channel.Reader, () => RemoveSubscriber(id));
    }

    private void RemoveSubscriber(Guid id)
    {
        lock (_lock)
        {
            if (_subscribers.Remove(id, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }
}

public sealed class RuntimeLogSubscription : IDisposable
{
    private readonly Action _dispose;
    private bool _disposed;

    internal RuntimeLogSubscription(ChannelReader<RuntimeLogEntry> reader, Action dispose)
    {
        Reader = reader;
        _dispose = dispose;
    }

    public ChannelReader<RuntimeLogEntry> Reader { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _dispose();
    }
}
