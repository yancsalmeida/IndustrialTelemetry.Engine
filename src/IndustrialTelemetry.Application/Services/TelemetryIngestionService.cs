using System.Diagnostics;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Application.Services;

public class TelemetryIngestionService
{
    private readonly IBatchRepository _batchRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IAlertRepository _alertRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ITelemetryChannel _channel;

    public TelemetryIngestionService(
        IBatchRepository batchRepo,
        ITelemetryRepository telemetryRepo,
        IAlertRepository alertRepo,
        IUnitOfWork unitOfWork,
        ITelemetryChannel channel)
    {
        _batchRepo = batchRepo;
        _telemetryRepo = telemetryRepo;
        _alertRepo = alertRepo;
        _unitOfWork = unitOfWork;
        _channel = channel;
    }

    /// <summary>
    /// Transactional ingestion pipeline: verifies batch status, evaluates real-time alert rules,
    /// and persists measurements and alerts atomically via EF Core.
    /// </summary>
    public async Task<IngestResultDto> IngestAsync(IngestTelemetryBatchDto dto, CancellationToken ct = default)
    {
        if (dto.Measurements == null || dto.Measurements.Count == 0)
        {
            return new IngestResultDto(0, 0);
        }

        var batch = await ValidateBatchForIngestionAsync(dto.BatchId, ct);
        var activeRules = await _alertRepo.GetActiveRulesAsync(ct);
        var measurements = MapToMeasurements(dto);
        var triggeredAlerts = new List<ProcessAlert>();

        foreach (var measurement in measurements)
        {
            // Real-time automated threshold rule evaluation
            foreach (var rule in activeRules)
            {
                var alert = rule.Evaluate(measurement);
                if (alert != null)
                {
                    triggeredAlerts.Add(alert);
                }
            }
        }

        await _telemetryRepo.IngestBatchAsync(measurements, ct);

        if (triggeredAlerts.Count > 0)
        {
            await _alertRepo.AddAlertsAsync(triggeredAlerts, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return new IngestResultDto(measurements.Count, triggeredAlerts.Count);
    }

    /// <summary>
    /// Asynchronous non-blocking streaming ingestion via bounded in-memory channel.
    /// Fast-paths incoming telemetry into the channel buffer and returns immediately (202 Accepted).
    /// </summary>
    public async Task<ChannelIngestResultDto> IngestChannelAsync(IngestTelemetryBatchDto dto, CancellationToken ct = default)
    {
        if (dto.Measurements == null || dto.Measurements.Count == 0)
        {
            return new ChannelIngestResultDto(0, _channel.CurrentCount);
        }

        await ValidateBatchForIngestionAsync(dto.BatchId, ct);
        var measurements = MapToMeasurements(dto);
        var written = await _channel.WriteBatchAsync(measurements, ct);

        return new ChannelIngestResultDto(written, _channel.CurrentCount);
    }

    /// <summary>
    /// High-throughput direct bulk copy ingestion bypassing EF Core change-tracking overhead
    /// via PostgreSQL Binary COPY (COPY ... FROM STDIN BINARY).
    /// </summary>
    public async Task<BulkIngestResultDto> IngestBulkAsync(IngestTelemetryBatchDto dto, CancellationToken ct = default)
    {
        if (dto.Measurements == null || dto.Measurements.Count == 0)
        {
            return new BulkIngestResultDto(0, 0.0, 0.0);
        }

        await ValidateBatchForIngestionAsync(dto.BatchId, ct);
        var measurements = MapToMeasurements(dto);

        var stopwatch = Stopwatch.StartNew();
        var rowsImported = await _telemetryRepo.BulkCopyAsync(measurements, ct);
        stopwatch.Stop();

        var elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
        var rps = stopwatch.Elapsed.TotalSeconds > 0 ? rowsImported / stopwatch.Elapsed.TotalSeconds : rowsImported * 1000.0;

        return new BulkIngestResultDto(rowsImported, Math.Round(elapsedMs, 2), Math.Round(rps, 2));
    }

    /// <summary>
    /// Inspects the live status of the bounded in-memory telemetry ingestion channel.
    /// </summary>
    public ChannelStatusDto GetChannelStatus()
    {
        return _channel.GetStatus();
    }

    public async Task<IReadOnlyList<TelemetryMeasurementResponseDto>> GetMeasurementsAsync(
        Guid batchId,
        string? sensorTag = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        var records = await _telemetryRepo.GetByBatchIdAsync(batchId, sensorTag, limit, ct);
        return records.Select(r => new TelemetryMeasurementResponseDto(
            r.Id,
            r.BatchId,
            r.TimestampUtc,
            r.SensorTag,
            r.Value,
            r.Unit.ToString(),
            r.Quality.ToString()
        )).ToList();
    }

    private async Task<Batch> ValidateBatchForIngestionAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await _batchRepo.GetByIdAsync(batchId, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), batchId);

        if (batch.Status != BatchStatus.InProgress)
        {
            throw new DomainValidationException(
                $"Telemetry can only be ingested for batches in 'InProgress' state. Current status: '{batch.Status}'.");
        }

        return batch;
    }

    private static List<TelemetryMeasurement> MapToMeasurements(IngestTelemetryBatchDto dto)
    {
        if (dto.Measurements == null || dto.Measurements.Count == 0)
            return new List<TelemetryMeasurement>();

        var measurements = new List<TelemetryMeasurement>(dto.Measurements.Count);
        foreach (var m in dto.Measurements)
        {
            if (!Enum.TryParse<MeasurementUnit>(m.Unit, true, out var unit))
            {
                unit = MeasurementUnit.Celsius;
            }

            var quality = TelemetryQuality.Good;
            if (!string.IsNullOrEmpty(m.Quality) && Enum.TryParse<TelemetryQuality>(m.Quality, true, out var q))
            {
                quality = q;
            }

            var measurement = TelemetryMeasurement.Create(
                dto.BatchId,
                m.TimestampUtc,
                m.SensorTag,
                m.Value,
                unit,
                quality
            );
            measurements.Add(measurement);
        }

        return measurements;
    }
}
