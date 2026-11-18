using System.Text.Json;
using System.Text.Json.Serialization;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Infrastructure.Outbox;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IndustrialTelemetry.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ObservabilityController : ControllerBase
{
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IAlertRepository _alertRepo;
    private readonly IOutboxRepository _outboxRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITelemetryChannel _channel;
    private readonly IObservabilityStream _stream;
    private readonly IOutboxDispatchMetrics? _dispatchMetrics;
    private readonly SpcAnalyticsService? _spcAnalytics;
    private readonly int _maxRetries;

    public ObservabilityController(
        ITelemetryRepository telemetryRepo,
        IAlertRepository alertRepo,
        IOutboxRepository outboxRepo,
        IUnitOfWork unitOfWork,
        ITelemetryChannel channel,
        IObservabilityStream stream,
        IOutboxDispatchMetrics? dispatchMetrics = null,
        IOptions<OutboxOptions>? outboxOptions = null,
        SpcAnalyticsService? spcAnalytics = null)
    {
        _telemetryRepo = telemetryRepo;
        _alertRepo = alertRepo;
        _outboxRepo = outboxRepo;
        _unitOfWork = unitOfWork;
        _channel = channel;
        _stream = stream;
        _dispatchMetrics = dispatchMetrics;
        _spcAnalytics = spcAnalytics;
        _maxRetries = outboxOptions?.Value?.MaxRetries > 0 ? outboxOptions.Value.MaxRetries : 5;
    }

    [HttpGet("overview")]
    [ProducesResponseType(typeof(ObservabilityOverviewDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOverview(CancellationToken ct = default)
    {
        long totalMeasurements = await _telemetryRepo.GetTotalCountAsync(ct);
        var summaries = await _telemetryRepo.GetSensorSummariesAsync(rateWindowSeconds: 60, ct: ct);
        var (totalAlerts, unackedAlerts, criticalAlerts) = await _alertRepo.GetGlobalCountsAsync(ct);
        var (pending, processed, deadLetter) = await _outboxRepo.GetSummaryCountsAsync(_maxRetries, ct);

        var ch = _channel.GetStatus();
        var channelDto = new ChannelStatusDto(
            ch.CurrentCount,
            ch.Capacity,
            ch.TotalIngested,
            ch.TotalDrained,
            ch.TotalDropped,
            ch.IsWriterCompleted);

        var streamStats = _stream.GetStats();

        return Ok(new ObservabilityOverviewDto(
            ServerTimestampUtc: DateTime.UtcNow,
            TotalMeasurements: totalMeasurements,
            ActiveSensorTagsCount: summaries.Count,
            TotalAlerts: totalAlerts,
            UnacknowledgedAlerts: unackedAlerts,
            CriticalAlerts: criticalAlerts,
            ChannelStatus: channelDto,
            OutboxPendingCount: pending,
            OutboxProcessedCount: processed,
            OutboxDeadLetterCount: deadLetter,
            LiveOutboxDispatchedCount: _dispatchMetrics?.TotalDispatchedCount ?? 0,
            ActiveSseSubscribers: streamStats.ActiveSubscribers,
            TotalSseEventsPublished: streamStats.TotalEventsPublished));
    }

    [HttpGet("sensors")]
    [ProducesResponseType(typeof(IReadOnlyList<SensorSummaryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSensors(
        [FromQuery] Guid? batchId = null,
        [FromQuery] int rateWindowSeconds = 60,
        CancellationToken ct = default)
    {
        var records = await _telemetryRepo.GetSensorSummariesAsync(batchId, rateWindowSeconds, ct);
        var dtos = records.Select(r => new SensorSummaryDto(
            r.SensorTag,
            r.Unit,
            r.Quality,
            r.LatestValue,
            r.LatestTimestampUtc,
            r.MinRecent,
            r.MaxRecent,
            r.AvgRecent,
            r.SampleCount,
            r.RateOfChangePerMin)).ToList();

        return Ok(dtos);
    }

    [HttpGet("timeseries")]
    [ProducesResponseType(typeof(IReadOnlyList<TimeSeriesPointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTimeSeries(
        [FromQuery] string sensorTag,
        [FromQuery] Guid? batchId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int limit = 300,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sensorTag))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing Sensor Tag",
                Detail = "Query parameter 'sensorTag' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var rows = await _telemetryRepo.GetTimeSeriesAsync(sensorTag, batchId, fromUtc, toUtc, limit, ct);
        var dtos = rows.Select(r => new TimeSeriesPointDto(
            r.Id,
            r.BatchId,
            r.TimestampUtc,
            r.Value,
            r.Unit.ToString(),
            r.Quality.ToString())).ToList();

        return Ok(dtos);
    }

    [HttpGet("analytics/downsample")]
    [ProducesResponseType(typeof(IReadOnlyList<DownsampledBucketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDownsampledSeries(
        [FromQuery] string sensorTag,
        [FromQuery] int bucketSeconds = 5,
        [FromQuery] Guid? batchId = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        [FromQuery] int maxBuckets = 240,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sensorTag))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing Sensor Tag",
                Detail = "Query parameter 'sensorTag' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var buckets = await _telemetryRepo.GetDownsampledSeriesAsync(
            sensorTag, bucketSeconds, batchId, fromUtc, toUtc, maxBuckets, ct);

        return Ok(buckets.Select(b => new DownsampledBucketDto(
            b.BucketStartUtc,
            b.AvgValue,
            b.MinValue,
            b.MaxValue,
            b.SampleCount)).ToList());
    }

    [HttpGet("analytics/spc")]
    [ProducesResponseType(typeof(SpcControlChartDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSpcAnalysis(
        [FromQuery] string sensorTag,
        [FromQuery] Guid? batchId = null,
        [FromQuery] double lambda = 0.2,
        [FromQuery] double sigmaMultiplier = 3.0,
        [FromQuery] int limit = 300,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sensorTag))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Missing Sensor Tag",
                Detail = "Query parameter 'sensorTag' is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var svc = _spcAnalytics ?? new SpcAnalyticsService(_telemetryRepo);
        var result = await svc.AnalyzeSpcAsync(sensorTag, batchId, lambda, sigmaMultiplier, limit, ct);
        return Ok(result);
    }

    [HttpGet("analytics/golden-batch")]
    [ProducesResponseType(typeof(GoldenBatchComparisonDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CompareGoldenBatch(
        [FromQuery] string sensorTag,
        [FromQuery] Guid referenceBatchId,
        [FromQuery] Guid candidateBatchId,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sensorTag) || referenceBatchId == Guid.Empty || candidateBatchId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid Golden Batch Query",
                Detail = "Parameters 'sensorTag', 'referenceBatchId', and 'candidateBatchId' are required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var svc = _spcAnalytics ?? new SpcAnalyticsService(_telemetryRepo);
        var result = await svc.CompareGoldenBatchAsync(sensorTag, referenceBatchId, candidateBatchId, limit, ct);
        return Ok(result);
    }

    [HttpGet("alerts")]
    [ProducesResponseType(typeof(IReadOnlyList<ProcessAlertResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRecentAlerts(
        [FromQuery] Guid? batchId = null,
        [FromQuery] string? severity = null,
        [FromQuery] bool? unacknowledgedOnly = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var alerts = await _alertRepo.GetRecentAlertsAsync(batchId, severity, unacknowledgedOnly, limit, ct);
        var dtos = alerts.Select(a => new ProcessAlertResponseDto(
            a.Id,
            a.BatchId,
            a.SensorTag,
            a.TimestampUtc,
            a.TriggeredValue,
            a.ThresholdValue,
            a.Severity.ToString(),
            a.Message,
            a.Acknowledged,
            a.AcknowledgedAtUtc,
            a.AcknowledgedBy)).ToList();

        return Ok(dtos);
    }

    [HttpGet("outbox")]
    [ProducesResponseType(typeof(OutboxSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutboxAudit(
        [FromQuery] bool? pendingOnly = null,
        [FromQuery] bool? deadLetterOnly = null,
        [FromQuery] int limit = 100,
        CancellationToken ct = default)
    {
        var (pending, processed, deadLetter) = await _outboxRepo.GetSummaryCountsAsync(_maxRetries, ct);
        var msgs = await _outboxRepo.GetRecentMessagesAsync(pendingOnly, deadLetterOnly, _maxRetries, limit, ct);

        var msgDtos = msgs.Select(m =>
        {
            string status = m.ProcessedOnUtc.HasValue
                ? "PROCESSED"
                : (m.RetryCount >= _maxRetries ? "DEAD_LETTER" : "PENDING");

            return new OutboxMessageDto(
                m.Id,
                m.EventType,
                m.AggregateType,
                m.AggregateId,
                m.Payload,
                m.OccurredOnUtc,
                m.ProcessedOnUtc,
                m.RetryCount,
                m.LastError,
                m.CorrelationId,
                status);
        }).ToList();

        return Ok(new OutboxSummaryDto(
            PendingCount: pending,
            ProcessedCount: processed,
            DeadLetterCount: deadLetter,
            LiveDispatchedCount: _dispatchMetrics?.TotalDispatchedCount ?? 0,
            WebhookSuccessCount: _dispatchMetrics?.WebhookSuccessCount ?? 0,
            WebhookFailureCount: _dispatchMetrics?.WebhookFailureCount ?? 0,
            ConfiguredWebhooks: _dispatchMetrics?.ConfiguredWebhooks ?? Array.Empty<string>(),
            RecentMessages: msgDtos));
    }

    [HttpPost("outbox/requeue-failed")]
    [ProducesResponseType(typeof(RequeueDeadLettersResultDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> RequeueFailedOutboxMessages(CancellationToken ct = default)
    {
        int count = await _outboxRepo.RequeueFailedMessagesAsync(_maxRetries, ct);
        if (count > 0)
            await _unitOfWork.SaveChangesAsync(ct);

        return Ok(new RequeueDeadLettersResultDto(count));
    }

    /// <summary>
    /// Live Server-Sent Events (SSE) stream pushing real-time telemetry batch flushes,
    /// triggered process alerts, and dispatched Transactional Outbox events.
    /// </summary>
    [HttpGet("stream")]
    public async Task StreamEvents(CancellationToken ct)
    {
        Response.Headers.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await WriteSseEventAsync(
            new ObservabilityEventRecord(
                EventType: "connected",
                TimestampUtc: DateTime.UtcNow,
                Summary: "Subscribed to IndustrialTelemetry live SSE stream",
                Data: new { maxRetries = _maxRetries }),
            ct);

        var recent = _stream.GetStats().RecentEvents.TakeLast(10);
        foreach (var pastEvt in recent)
        {
            await WriteSseEventAsync(pastEvt, ct);
        }

        await foreach (var evt in _stream.SubscribeAsync(ct))
        {
            await WriteSseEventAsync(evt, ct);
        }
    }

    private async Task WriteSseEventAsync(ObservabilityEventRecord evt, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(evt, SseJsonOptions);
        await Response.WriteAsync($"event: {evt.EventType}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
