namespace IndustrialTelemetry.Application.DTOs;

public record SensorMeasurementDto(
    DateTime TimestampUtc,
    string SensorTag,
    double Value,
    string Unit,
    string? Quality
);

public record IngestTelemetryBatchDto(
    Guid BatchId,
    List<SensorMeasurementDto> Measurements
);

public record TelemetryMeasurementResponseDto(
    long Id,
    Guid BatchId,
    DateTime TimestampUtc,
    string SensorTag,
    double Value,
    string Unit,
    string Quality
);

public record BatchSummaryAnalyticsDto(
    Guid BatchId,
    string BatchCode,
    string ProductRecipe,
    string ReactorId,
    string Status,
    DateTime? StartTimeUtc,
    DateTime? EndTimeUtc,
    TimeSpan? Duration,
    double? AvgTemperatureC,
    double? MaxTemperatureC,
    double? MaxPressureBar,
    int TotalMeasurements,
    int TotalAlerts,
    int CriticalAlerts
);

public record IngestResultDto(
    int MeasurementsIngested,
    int AlertsTriggered
);

public record ChannelIngestResultDto(
    int AcceptedCount,
    int CurrentQueueDepth
);

public record BulkIngestResultDto(
    int RowsImported,
    double ElapsedMilliseconds,
    double RecordsPerSecond
);

public record ChannelStatusDto(
    int CurrentCount,
    int Capacity,
    long TotalIngested,
    long TotalDrained,
    long TotalDropped,
    bool IsWriterCompleted
);

public enum WireIngestMode
{
    Channel = 0,
    Transactional = 1,
    Bulk = 2
}

public record WireIngestResultDto(
    int TotalLinesParsed,
    int ValidMeasurements,
    int SkippedLines,
    double ElapsedMilliseconds,
    double RecordsPerSecond,
    string IngestMode,
    int AlertsTriggered = 0,
    int ChannelQueueDepth = 0
);

