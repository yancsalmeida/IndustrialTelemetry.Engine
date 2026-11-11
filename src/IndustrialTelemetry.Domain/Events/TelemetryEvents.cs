namespace IndustrialTelemetry.Domain.Events;

public sealed record TelemetryBatchIngestedDomainEvent(
    Guid BatchId,
    int MeasurementCount,
    DateTime FirstTimestampUtc,
    DateTime LastTimestampUtc,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}
