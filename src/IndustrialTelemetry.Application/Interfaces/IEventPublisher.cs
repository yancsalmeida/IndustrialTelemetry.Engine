using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

/// <summary>
/// Dispatches transactional outbox messages to external distributed message brokers
/// (e.g. Apache Kafka, RabbitMQ, Azure Service Bus, or MQTT event hubs).
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken ct = default);
}
