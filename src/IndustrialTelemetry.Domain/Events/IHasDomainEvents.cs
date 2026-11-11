namespace IndustrialTelemetry.Domain.Events;

/// <summary>
/// Contract for aggregate roots and entities that collect domain events during business operations.
/// Events are atomically harvested and converted to transactional outbox messages during database commits.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
