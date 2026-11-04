namespace IndustrialTelemetry.Application.DTOs;

public record CreateAlertRuleDto(
    string SensorTag,
    double? MinThreshold,
    double? MaxThreshold,
    string Severity,
    string Description
);

public record AlertRuleResponseDto(
    Guid Id,
    string SensorTag,
    double? MinThreshold,
    double? MaxThreshold,
    string Severity,
    string Description,
    bool IsActive
);

public record ProcessAlertResponseDto(
    Guid Id,
    Guid BatchId,
    string SensorTag,
    DateTime TimestampUtc,
    double TriggeredValue,
    double ThresholdValue,
    string Severity,
    string Message,
    bool Acknowledged,
    DateTime? AcknowledgedAtUtc,
    string? AcknowledgedBy
);

public record AcknowledgeAlertDto(
    string OperatorId,
    DateTime? TimestampUtc
);
