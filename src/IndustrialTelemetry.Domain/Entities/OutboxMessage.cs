using System.Text.Json;
using IndustrialTelemetry.Domain.Events;

namespace IndustrialTelemetry.Domain.Entities;

/// <summary>
/// Transactional Outbox Message stored in PostgreSQL within the same ACID transaction
/// as domain state changes. Guarantees At-Least-Once delivery to external message brokers
/// (Kafka, RabbitMQ, MQTT) without distributed dual-write inconsistencies.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string AggregateType { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTime OccurredOnUtc { get; private set; }
    public DateTime? ProcessedOnUtc { get; private set; }
    public int RetryCount { get; private set; }
    public string? LastError { get; private set; }
    public string? CorrelationId { get; private set; }

    // Required by EF Core
    private OutboxMessage() { }

    public static OutboxMessage Create(
        string eventType,
        string aggregateType,
        string aggregateId,
        string payload,
        DateTime occurredOnUtc,
        string? correlationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateType);
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType.Trim(),
            AggregateType = aggregateType.Trim(),
            AggregateId = aggregateId.Trim(),
            Payload = payload,
            OccurredOnUtc = occurredOnUtc.Kind == DateTimeKind.Utc ? occurredOnUtc : occurredOnUtc.ToUniversalTime(),
            ProcessedOnUtc = null,
            RetryCount = 0,
            LastError = null,
            CorrelationId = correlationId?.Trim()
        };
    }

    public static OutboxMessage FromDomainEvent<TEvent>(
        TEvent domainEvent,
        string aggregateType,
        string aggregateId,
        string? correlationId = null) where TEvent : IDomainEvent
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        string eventType = domainEvent.GetType().Name;
        string payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());

        return Create(eventType, aggregateType, aggregateId, payload, domainEvent.OccurredOnUtc, correlationId);
    }

    public void MarkAsProcessed()
    {
        ProcessedOnUtc = DateTime.UtcNow;
        LastError = null;
    }

    public void RecordFailure(string error)
    {
        RetryCount++;
        LastError = error?.Length > 2048 ? error[..2048] : error;
    }

    public void ResetForRetry()
    {
        ProcessedOnUtc = null;
    }
}
