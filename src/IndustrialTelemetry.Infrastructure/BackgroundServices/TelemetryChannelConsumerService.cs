using System.Diagnostics;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Infrastructure.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IndustrialTelemetry.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker continuously draining the in-memory telemetry channel in high-throughput batches
/// and persisting them via PostgreSQL Binary COPY.
/// </summary>
public class TelemetryChannelConsumerService : BackgroundService
{
    private readonly ITelemetryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryChannelConsumerService> _logger;
    private readonly int _batchSize;
    private readonly int _flushIntervalMs;

    public TelemetryChannelConsumerService(
        ITelemetryChannel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<TelemetryChannelConsumerService> logger,
        IOptions<TelemetryChannelOptions>? options = null)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _batchSize = options?.Value?.BatchSize > 0 ? options.Value.BatchSize : 1000;
        _flushIntervalMs = options?.Value?.FlushIntervalMs > 0 ? options.Value.FlushIntervalMs : 50;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "TelemetryChannelConsumerService started. BatchSize: {BatchSize}, FlushInterval: {FlushInterval}ms.",
            _batchSize, _flushIntervalMs);

        var batch = new List<TelemetryMeasurement>(_batchSize);
        var stopwatch = Stopwatch.StartNew();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                while (batch.Count < _batchSize)
                {
                    if (_channel.TryRead(out var item))
                    {
                        batch.Add(item);
                        continue;
                    }

                    if (batch.Count > 0)
                    {
                        var elapsed = (int)stopwatch.ElapsedMilliseconds;
                        var remainingTimeout = _flushIntervalMs - elapsed;
                        if (remainingTimeout <= 0)
                        {
                            break;
                        }

                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        cts.CancelAfter(remainingTimeout);

                        try
                        {
                            if (await _channel.WaitToReadAsync(cts.Token) && _channel.TryRead(out item))
                            {
                                batch.Add(item);
                            }
                        }
                        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                        {
                            // Window timeout reached with non-empty batch
                            break;
                        }
                    }
                    else
                    {
                        // Batch is empty, await next measurement indefinitely until token cancelled
                        if (await _channel.WaitToReadAsync(stoppingToken) && _channel.TryRead(out item))
                        {
                            batch.Add(item);
                            stopwatch.Restart();
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    var itemsToFlush = batch.ToArray();
                    batch.Clear();
                    stopwatch.Restart();
                    await FlushBatchAsync(itemsToFlush, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing telemetry channel batch.");
                await Task.Delay(100, stoppingToken);
            }
        }

        // Graceful drain on shutdown
        await DrainRemainingOnShutdownAsync();
        _logger.LogInformation("TelemetryChannelConsumerService stopped successfully.");
    }

    private async Task FlushBatchAsync(IReadOnlyList<TelemetryMeasurement> batch, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<ITelemetryRepository>();
            var count = await repository.BulkCopyAsync(batch, ct);
            sw.Stop();

            var rate = sw.Elapsed.TotalSeconds > 0 ? count / sw.Elapsed.TotalSeconds : count * 1000.0;
            _logger.LogDebug(
                "Flushed batch of {Count} measurements in {ElapsedMs:F2} ms ({Rate:F0} rec/s).",
                count, sw.Elapsed.TotalMilliseconds, rate);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Failed to flush telemetry batch of {Count} items.", batch.Count);
            throw;
        }
    }

    private async Task DrainRemainingOnShutdownAsync()
    {
        var remainingBatch = new List<TelemetryMeasurement>();
        while (_channel.TryRead(out var item))
        {
            remainingBatch.Add(item);
        }

        if (remainingBatch.Count > 0)
        {
            _logger.LogInformation("Draining {Count} remaining channel items on shutdown...", remainingBatch.Count);
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await FlushBatchAsync(remainingBatch, cts.Token);
                _logger.LogInformation("Shutdown drain completed successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to drain remaining channel items on shutdown.");
            }
        }
    }
}
