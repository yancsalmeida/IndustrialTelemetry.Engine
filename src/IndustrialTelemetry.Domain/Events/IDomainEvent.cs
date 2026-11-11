namespace IndustrialTelemetry.Domain.Events;

/// <summary>
/// Marker interface for all domain events within the telemetry system.
/// Every domain event represents an immutable state change that has occurred.
/// </summary>
public interface IDomainEvent
{
    Guid EventId { get; }
    DateTime OccurredOnUtc { get; }
}
