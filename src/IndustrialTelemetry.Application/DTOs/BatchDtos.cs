namespace IndustrialTelemetry.Application.DTOs;

public record CreateBatchDto(
    string Code,
    string ProductRecipe,
    string ReactorId,
    double TargetVolumeM3,
    string? OperatorNotes
);

public record StartBatchDto(
    DateTime? TimestampUtc
);

public record AbortBatchDto(
    string Reason,
    DateTime? TimestampUtc
);

public record CompleteBatchDto(
    DateTime? TimestampUtc
);

public record BatchResponseDto(
    Guid Id,
    string Code,
    string ProductRecipe,
    string ReactorId,
    double TargetVolumeM3,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? StartTimeUtc,
    DateTime? EndTimeUtc,
    string? AbortReason,
    string? OperatorNotes,
    int AlertCount
);
