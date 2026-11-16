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
