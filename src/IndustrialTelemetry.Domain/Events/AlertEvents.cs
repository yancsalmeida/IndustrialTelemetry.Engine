using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Domain.Events;

public sealed record ProcessAlertTriggeredDomainEvent(
    Guid AlertId,
    Guid BatchId,
    string SensorTag,
    DateTime TimestampUtc,
    double TriggeredValue,
    double ThresholdValue,
    AlertSeverity Severity,
    string Message,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}

public sealed record ProcessAlertAcknowledgedDomainEvent(
    Guid AlertId,
    Guid BatchId,
    string AcknowledgedBy,
    DateTime AcknowledgedAtUtc,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}
