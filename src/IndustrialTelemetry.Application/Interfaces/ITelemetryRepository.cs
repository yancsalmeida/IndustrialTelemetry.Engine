using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

public record SensorAggregate(string SensorTag, double Average, double Min, double Max, int Count);

public interface ITelemetryRepository
{
    Task IngestBatchAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetryMeasurement>> GetByBatchIdAsync(Guid batchId, string? sensorTag = null, int limit = 500, CancellationToken ct = default);
    Task<IReadOnlyList<SensorAggregate>> GetSensorAggregatesByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<int> GetCountByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<int> BulkCopyAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default);
}
