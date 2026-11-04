using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Application.Services;

public class BatchService
{
    private readonly IBatchRepository _batchRepo;
    private readonly ITelemetryRepository _telemetryRepo;
    private readonly IAlertRepository _alertRepo;
    private readonly IUnitOfWork _unitOfWork;

    public BatchService(
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

    public async Task<BatchResponseDto> CreateBatchAsync(CreateBatchDto dto, CancellationToken ct = default)
    {
        var existing = await _batchRepo.GetByCodeAsync(dto.Code, ct);
        if (existing != null)
        {
            throw new DomainValidationException($"A batch with code '{dto.Code}' already exists.");
        }

        var batch = Batch.Create(
            dto.Code,
            dto.ProductRecipe,
            dto.ReactorId,
            dto.TargetVolumeM3,
            dto.OperatorNotes);

        await _batchRepo.AddAsync(batch, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return MapToResponse(batch, 0);
    }

    public async Task<BatchResponseDto> StartBatchAsync(Guid id, StartBatchDto dto, CancellationToken ct = default)
    {
        var batch = await _batchRepo.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), id);

        var timestamp = dto?.TimestampUtc ?? DateTime.UtcNow;
        batch.Start(timestamp);

        await _batchRepo.UpdateAsync(batch, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var alertCount = await _alertRepo.GetCountByBatchAsync(batch.Id, ct);
        return MapToResponse(batch, alertCount);
    }

    public async Task<BatchResponseDto> CompleteBatchAsync(Guid id, CompleteBatchDto dto, CancellationToken ct = default)
    {
        var batch = await _batchRepo.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), id);

        var timestamp = dto?.TimestampUtc ?? DateTime.UtcNow;
        batch.Complete(timestamp);

        await _batchRepo.UpdateAsync(batch, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var alertCount = await _alertRepo.GetCountByBatchAsync(batch.Id, ct);
        return MapToResponse(batch, alertCount);
    }

    public async Task<BatchResponseDto> AbortBatchAsync(Guid id, AbortBatchDto dto, CancellationToken ct = default)
    {
        var batch = await _batchRepo.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), id);

        var timestamp = dto?.TimestampUtc ?? DateTime.UtcNow;
        batch.Abort(dto?.Reason ?? string.Empty, timestamp);

        await _batchRepo.UpdateAsync(batch, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var alertCount = await _alertRepo.GetCountByBatchAsync(batch.Id, ct);
        return MapToResponse(batch, alertCount);
    }

    public async Task<BatchResponseDto> GetBatchByIdAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _batchRepo.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), id);

        var alertCount = await _alertRepo.GetCountByBatchAsync(batch.Id, ct);
        return MapToResponse(batch, alertCount);
    }

    public async Task<IReadOnlyList<BatchResponseDto>> ListBatchesAsync(BatchStatus? status = null, int limit = 50, CancellationToken ct = default)
    {
        var batches = await _batchRepo.ListAsync(status, limit, ct);
        var result = new List<BatchResponseDto>(batches.Count);

        foreach (var b in batches)
        {
            var alertCount = await _alertRepo.GetCountByBatchAsync(b.Id, ct);
            result.Add(MapToResponse(b, alertCount));
        }

        return result;
    }

    public async Task<BatchSummaryAnalyticsDto> GetBatchSummaryAsync(Guid id, CancellationToken ct = default)
    {
        var batch = await _batchRepo.GetByIdAsync(id, ct)
            ?? throw new EntityNotFoundException(nameof(Batch), id);

        TimeSpan? duration = null;
        if (batch.StartTimeUtc.HasValue)
        {
            var end = batch.EndTimeUtc ?? DateTime.UtcNow;
            duration = end - batch.StartTimeUtc.Value;
        }

        var aggregates = await _telemetryRepo.GetSensorAggregatesByBatchAsync(id, ct);
        var totalMeasurements = await _telemetryRepo.GetCountByBatchAsync(id, ct);
        var alerts = await _alertRepo.GetAlertsByBatchIdAsync(id, ct);

        // Find temperature aggregate (e.g. tag starting with TT or temp)
        var tempAgg = aggregates.FirstOrDefault(a => a.SensorTag.StartsWith("TT", StringComparison.OrdinalIgnoreCase)
                                                  || a.SensorTag.Contains("TEMP", StringComparison.OrdinalIgnoreCase));

        // Find pressure aggregate (e.g. tag starting with PT or press)
        var pressAgg = aggregates.FirstOrDefault(a => a.SensorTag.StartsWith("PT", StringComparison.OrdinalIgnoreCase)
                                                   || a.SensorTag.Contains("PRESS", StringComparison.OrdinalIgnoreCase));

        return new BatchSummaryAnalyticsDto(
            BatchId: batch.Id,
            BatchCode: batch.Code,
            ProductRecipe: batch.ProductRecipe,
            ReactorId: batch.ReactorId,
            Status: batch.Status.ToString(),
            StartTimeUtc: batch.StartTimeUtc,
            EndTimeUtc: batch.EndTimeUtc,
            Duration: duration,
            AvgTemperatureC: tempAgg?.Average,
            MaxTemperatureC: tempAgg?.Max,
            MaxPressureBar: pressAgg?.Max,
            TotalMeasurements: totalMeasurements,
            TotalAlerts: alerts.Count,
            CriticalAlerts: alerts.Count(a => a.Severity == AlertSeverity.Critical)
        );
    }

    private static BatchResponseDto MapToResponse(Batch batch, int alertCount) =>
        new(
            batch.Id,
            batch.Code,
            batch.ProductRecipe,
            batch.ReactorId,
            batch.TargetVolumeM3,
            batch.Status.ToString(),
            batch.CreatedAtUtc,
            batch.StartTimeUtc,
            batch.EndTimeUtc,
            batch.AbortReason,
            batch.OperatorNotes,
            alertCount
        );
}
