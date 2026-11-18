namespace IndustrialTelemetry.Application.DTOs;

public record SensorSummaryDto(
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

public record TimeSeriesPointDto(
    long Id,
    Guid BatchId,
    DateTime TimestampUtc,
    double Value,
    string Unit,
    string Quality
);

public record OutboxMessageDto(
    Guid Id,
    string EventType,
    string AggregateType,
    string AggregateId,
    string Payload,
    DateTime OccurredOnUtc,
    DateTime? ProcessedOnUtc,
    int RetryCount,
    string? LastError,
    string? CorrelationId,
    string Status
);

public record OutboxSummaryDto(
    int PendingCount,
    int ProcessedCount,
    int DeadLetterCount,
    long LiveDispatchedCount,
    long WebhookSuccessCount,
    long WebhookFailureCount,
    IReadOnlyList<string> ConfiguredWebhooks,
    IReadOnlyList<OutboxMessageDto> RecentMessages
);

public record ObservabilityOverviewDto(
    DateTime ServerTimestampUtc,
    long TotalMeasurements,
    int ActiveSensorTagsCount,
    int TotalAlerts,
    int UnacknowledgedAlerts,
    int CriticalAlerts,
    ChannelStatusDto ChannelStatus,
    int OutboxPendingCount,
    int OutboxProcessedCount,
    int OutboxDeadLetterCount,
    long LiveOutboxDispatchedCount,
    int ActiveSseSubscribers,
    long TotalSseEventsPublished
);

public record RequeueDeadLettersResultDto(
    int RequeuedCount
);

public record DownsampledBucketDto(
    DateTime BucketStartUtc,
    double AvgValue,
    double MinValue,
    double MaxValue,
    int SampleCount
);

public record SpcPointDto(
    DateTime TimestampUtc,
    double RawValue,
    double EwmaValue,
    double EwmaUpperLimit,
    double EwmaLowerLimit,
    bool ShewhartOutOfControl,
    bool EwmaOutOfControl
);

public record SpcControlChartDto(
    string SensorTag,
    Guid? BatchId,
    int SampleCount,
    double Lambda,
    double SigmaMultiplier,
    double Mean,
    double StdDev,
    double ShewhartUcl,
    double ShewhartLcl,
    int ShewhartBreachCount,
    int EwmaBreachCount,
    double MaxZScore,
    IReadOnlyList<SpcPointDto> Points
);

public record GoldenBatchPointDto(
    double ElapsedSeconds,
    double GoldenValue,
    double CandidateValue,
    double Residual
);

public record GoldenBatchComparisonDto(
    string SensorTag,
    Guid ReferenceBatchId,
    Guid CandidateBatchId,
    int AlignedPointCount,
    double Rmse,
    double Mae,
    double MaxAbsoluteDeviation,
    double MeanBias,
    IReadOnlyList<GoldenBatchPointDto> AlignedPoints
);
