using System.Net.Http.Json;
using System.Text.Json;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialTelemetry.Infrastructure.Outbox;

public interface IOutboxDispatchMetrics
{
    long TotalDispatchedCount { get; }
    long WebhookSuccessCount { get; }
    long WebhookFailureCount { get; }
    IReadOnlyList<string> ConfiguredWebhooks { get; }
}

/// <summary>
/// Production <see cref="IEventPublisher"/> dispatching transactional outbox events
/// to the live SSE <see cref="IObservabilityStream"/> and optional HTTP webhooks.
/// Failures on external webhook delivery throw so <see cref="BackgroundServices.OutboxProcessorBackgroundService"/>
/// records retry attempts and dead-letters cleanly.
/// </summary>
public sealed class LiveOutboxEventPublisher : IEventPublisher, IOutboxDispatchMetrics, IDisposable
{
    private readonly IObservabilityStream _stream;
    private readonly ILogger<LiveOutboxEventPublisher>? _logger;
    private readonly string[] _webhooks;
    private readonly int _timeoutMs;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    private long _totalDispatched;
    private long _webhookSuccess;
    private long _webhookFailure;

    public LiveOutboxEventPublisher(
        IObservabilityStream stream,
        IOptions<OutboxOptions>? options = null,
        ILogger<LiveOutboxEventPublisher>? logger = null,
        HttpMessageHandler? handler = null)
    {
        _stream = stream;
        _logger = logger;
        var opts = options?.Value ?? new OutboxOptions();
        _webhooks = (opts.WebhookUrls ?? [])
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _timeoutMs = Math.Clamp(opts.WebhookTimeoutMs, 250, 30000);
        _httpClient = handler is not null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_timeoutMs);
        _ownsHttpClient = handler is null;
    }

    public long TotalDispatchedCount => Interlocked.Read(ref _totalDispatched);
    public long WebhookSuccessCount => Interlocked.Read(ref _webhookSuccess);
    public long WebhookFailureCount => Interlocked.Read(ref _webhookFailure);
    public IReadOnlyList<string> ConfiguredWebhooks => _webhooks;

    public async Task PublishAsync(OutboxMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_webhooks.Length > 0)
        {
            object? parsedPayload = null;
            try
            {
                parsedPayload = JsonSerializer.Deserialize<JsonElement>(message.Payload);
            }
            catch
            {
                parsedPayload = message.Payload;
            }

            var envelope = new
            {
                outboxMessageId = message.Id,
                eventType = message.EventType,
                aggregateType = message.AggregateType,
                aggregateId = message.AggregateId,
                occurredOnUtc = message.OccurredOnUtc,
                correlationId = message.CorrelationId,
                payload = parsedPayload
            };

            foreach (string url in _webhooks)
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, url)
                    {
                        Content = JsonContent.Create(envelope)
                    };
                    req.Headers.TryAddWithoutValidation("X-Outbox-Message-Id", message.Id.ToString());
                    req.Headers.TryAddWithoutValidation("X-Outbox-Event-Type", message.EventType);
                    if (!string.IsNullOrWhiteSpace(message.CorrelationId))
                        req.Headers.TryAddWithoutValidation("X-Correlation-Id", message.CorrelationId);

                    using var resp = await _httpClient.SendAsync(req, ct).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();
                    Interlocked.Increment(ref _webhookSuccess);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Interlocked.Increment(ref _webhookFailure);
                    _logger?.LogWarning(ex,
                        "Outbox webhook dispatch failed for {MessageId} ({EventType}) -> {WebhookUrl}",
                        message.Id, message.EventType, url);
                    throw;
                }
            }
        }

        Interlocked.Increment(ref _totalDispatched);

        _stream.Publish(
            eventType: "outbox-dispatched",
            summary: $"{message.EventType} ({message.AggregateType}:{message.AggregateId})",
            data: new
            {
                id = message.Id,
                eventType = message.EventType,
                aggregateType = message.AggregateType,
                aggregateId = message.AggregateId,
                occurredOnUtc = message.OccurredOnUtc,
                correlationId = message.CorrelationId,
                payload = message.Payload
            });
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
