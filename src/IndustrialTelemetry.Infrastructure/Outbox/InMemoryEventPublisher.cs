using System.Collections.Concurrent;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace IndustrialTelemetry.Infrastructure.Outbox;

/// <summary>
/// In-memory event publisher implementation. Serves as default publisher for local development,
/// testing, and test harness simulation of transient broker disconnects.
/// </summary>
public class InMemoryEventPublisher : IEventPublisher
{
    private readonly ConcurrentBag<OutboxMessage> _publishedMessages = new();
    private readonly ILogger<InMemoryEventPublisher>? _logger;

    // Simulation hooks for testing broker network failures and retry policies
    public int FailNextPublishCount { get; set; }
    public Exception? FailureToThrow { get; set; }

    public IReadOnlyCollection<OutboxMessage> PublishedMessages => _publishedMessages.ToArray();

    public InMemoryEventPublisher(ILogger<InMemoryEventPublisher>? logger = null)
    {
        _logger = logger;
    }

    public Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (FailNextPublishCount > 0)
        {
            FailNextPublishCount--;
            _logger?.LogWarning(
                "Simulating broker transient failure for message {MessageId} ({EventType}). Remaining planned failures: {Remaining}",
                message.Id, message.EventType, FailNextPublishCount);

            throw FailureToThrow ?? new InvalidOperationException("Broker network failure / connection timed out.");
        }

        _publishedMessages.Add(message);
        _logger?.LogDebug(
            "Published outbox event {EventType} for {AggregateType}:{AggregateId} (MessageId: {MessageId})",
            message.EventType, message.AggregateType, message.AggregateId, message.Id);

        return Task.CompletedTask;
    }

    public void Clear()
    {
        _publishedMessages.Clear();
        FailNextPublishCount = 0;
        FailureToThrow = null;
    }
}
