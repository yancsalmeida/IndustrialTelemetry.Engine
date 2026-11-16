namespace IndustrialTelemetry.Application.Interfaces;

public record ObservabilityEventRecord(
    string EventType,
    DateTime TimestampUtc,
    string Summary,
    object Data
);

public record ObservabilityStreamStats(
    long TotalEventsPublished,
    int ActiveSubscribers,
    IReadOnlyList<ObservabilityEventRecord> RecentEvents
);

/// <summary>
/// Real-time pub/sub broadcast stream powering Server-Sent Events (SSE) observability clients
/// for telemetry ingestion batches, triggered process alerts, and dispatched outbox events.
/// </summary>
public interface IObservabilityStream
{
    void Publish(string eventType, string summary, object data);
    IAsyncEnumerable<ObservabilityEventRecord> SubscribeAsync(CancellationToken ct = default);
    ObservabilityStreamStats GetStats();
}
