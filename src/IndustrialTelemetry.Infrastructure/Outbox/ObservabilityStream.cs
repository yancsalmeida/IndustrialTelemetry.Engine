using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using IndustrialTelemetry.Application.Interfaces;

namespace IndustrialTelemetry.Infrastructure.Outbox;

/// <summary>
/// Thread-safe multi-subscriber pub/sub hub backing Server-Sent Events (SSE)
/// live observability streams with bounded per-client buffers.
/// </summary>
public sealed class ObservabilityStream : IObservabilityStream
{
    private const int MaxRecentEvents = 50;
    private readonly ConcurrentDictionary<Guid, Channel<ObservabilityEventRecord>> _subscribers = new();
    private readonly Queue<ObservabilityEventRecord> _recent = new();
    private readonly object _recentLock = new();
    private long _totalPublished;

    public void Publish(string eventType, string summary, object data)
    {
        var evt = new ObservabilityEventRecord(
            EventType: eventType,
            TimestampUtc: DateTime.UtcNow,
            Summary: summary,
            Data: data);

        Interlocked.Increment(ref _totalPublished);

        lock (_recentLock)
        {
            _recent.Enqueue(evt);
            while (_recent.Count > MaxRecentEvents)
                _recent.Dequeue();
        }

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(evt);
        }
    }

    public async IAsyncEnumerable<ObservabilityEventRecord> SubscribeAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ObservabilityEventRecord>(new BoundedChannelOptions(256)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        _subscribers[id] = channel;
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }

    public ObservabilityStreamStats GetStats()
    {
        ObservabilityEventRecord[] snapshot;
        lock (_recentLock)
        {
            snapshot = _recent.ToArray();
        }

        return new ObservabilityStreamStats(
            TotalEventsPublished: Interlocked.Read(ref _totalPublished),
            ActiveSubscribers: _subscribers.Count,
            RecentEvents: snapshot);
    }
}
