using System.Diagnostics;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Infrastructure.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialTelemetry.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker implementing the Transactional Outbox Pattern dispatcher.
/// Continuously queries pending outbox events committed atomically to PostgreSQL,
/// publishes them to the message broker, and marks them processed with retry tracking.
/// </summary>
public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;
    private readonly int _batchSize;
    private readonly int _pollIntervalMs;
    private readonly int _maxRetries;
    private readonly bool _autoCleanup;
    private readonly int _retentionDays;

    public OutboxProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorBackgroundService> logger,
        IOptions<OutboxOptions>? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchSize = options?.Value?.BatchSize > 0 ? options.Value.BatchSize : 50;
        _pollIntervalMs = options?.Value?.PollIntervalMs > 0 ? options.Value.PollIntervalMs : 250;
        _maxRetries = options?.Value?.MaxRetries > 0 ? options.Value.MaxRetries : 5;
        _autoCleanup = options?.Value?.AutoCleanup ?? false;
        _retentionDays = options?.Value?.RetentionDays > 0 ? options.Value.RetentionDays : 7;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "OutboxProcessorBackgroundService started. BatchSize: {BatchSize}, PollInterval: {Interval}ms, MaxRetries: {MaxRetries}",
            _batchSize, _pollIntervalMs, _maxRetries);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pollIntervalMs));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                await ProcessPendingBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in OutboxProcessorBackgroundService cycle.");
            }
        }

        _logger.LogInformation("OutboxProcessorBackgroundService stopped gracefully.");
    }

    /// <summary>
    /// Processes a single batch of pending outbox messages.
    /// Publicly accessible to allow deterministic unit and integration test assertions.
    /// </summary>
    public async Task<int> ProcessPendingBatchAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var outboxRepo = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var pending = await outboxRepo.GetPendingMessagesAsync(_batchSize, _maxRetries, ct);
        if (pending.Count == 0)
        {
            return 0;
        }

        var sw = Stopwatch.StartNew();
        int publishedCount = 0;
        int failedCount = 0;

        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ct);
                message.MarkAsProcessed();
                publishedCount++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failedCount++;
                message.RecordFailure(ex.Message);
                _logger.LogWarning(
                    ex,
                    "Failed to publish outbox message {MessageId} ({EventType}). RetryCount: {RetryCount}/{MaxRetries}",
                    message.Id, message.EventType, message.RetryCount, _maxRetries);
            }
        }

        await outboxRepo.UpdateRangeAsync(pending, ct);
        await unitOfWork.SaveChangesAsync(ct);
        sw.Stop();

        _logger.LogInformation(
            "Outbox batch completed in {ElapsedMs:F2}ms. Published: {Published}, Failed: {Failed}",
            sw.Elapsed.TotalMilliseconds, publishedCount, failedCount);

        if (_autoCleanup)
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
                var deleted = await outboxRepo.CleanupProcessedAsync(cutoff, ct);
                if (deleted > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} processed outbox messages older than {Days} days.", deleted, _retentionDays);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to run outbox auto-cleanup.");
            }
        }

        return publishedCount;
    }
}
