using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

public record SensorAggregate(string SensorTag, double Average, double Min, double Max, int Count);

public record SensorSummaryRecord(
    string SensorTag,
    string Unit,
    string Quality,
    double LatestValue,
    DateTime LatestTimestampUtc,
    double MinRecent,
    double MaxRecent,
    double AvgRecent,
    int SampleCount,
    double RateOfChangePerMin
);

public record DownsampledBucketRecord(
    DateTime BucketStartUtc,
    double AvgValue,
    double MinValue,
    double MaxValue,
    int SampleCount
);

public interface ITelemetryRepository
{
    Task IngestBatchAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetryMeasurement>> GetByBatchIdAsync(Guid batchId, string? sensorTag = null, int limit = 500, CancellationToken ct = default);
    Task<IReadOnlyList<SensorAggregate>> GetSensorAggregatesByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<int> GetCountByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<int> BulkCopyAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default);
    Task<long> GetTotalCountAsync(CancellationToken ct = default);
    Task<IReadOnlyList<SensorSummaryRecord>> GetSensorSummariesAsync(Guid? batchId = null, int rateWindowSeconds = 60, CancellationToken ct = default);
    Task<IReadOnlyList<TelemetryMeasurement>> GetTimeSeriesAsync(
        string sensorTag,
        Guid? batchId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 300,
        CancellationToken ct = default);
    Task<IReadOnlyList<DownsampledBucketRecord>> GetDownsampledSeriesAsync(
        string sensorTag,
        int bucketSeconds = 5,
        Guid? batchId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int maxBuckets = 240,
        CancellationToken ct = default);
}
