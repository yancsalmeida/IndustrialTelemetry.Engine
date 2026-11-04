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

    public TelemetryIngestionService(
        IBatchRepository batchRepo,
        ITelemetryRepository telemetryRepo,
        IAlertRepository alertRepo,
        IUnitOfWork unitOfWork)
    {
        _batchRepo = batchRepo;
        _telemetryRepo = telemetryRepo;
        _alertRepo = alertRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<IngestResultDto> IngestAsync(IngestTelemetryBatchDto dto, CancellationToken ct = default)
    {
        if (dto.Measurements == null || dto.Measurements.Count == 0)
        {
            return new IngestResultDto(0, 0);
        }

        var batch = await _batchRepo.GetByIdAsync(dto.BatchId, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), dto.BatchId);

        if (batch.Status != BatchStatus.InProgress)
        {
            throw new DomainValidationException(
                $"Telemetry can only be ingested for batches in 'InProgress' state. Current status: '{batch.Status}'.");
        }

        var activeRules = await _alertRepo.GetActiveRulesAsync(ct);
        var measurements = new List<TelemetryMeasurement>(dto.Measurements.Count);
        var triggeredAlerts = new List<ProcessAlert>();

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
}
