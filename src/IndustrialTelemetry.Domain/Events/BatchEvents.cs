using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Domain.Events;

public sealed record BatchCreatedDomainEvent(
    Guid BatchId,
    string Code,
    string ProductRecipe,
    string ReactorId,
    double TargetVolumeM3,
    string? OperatorNotes,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}

public sealed record BatchStartedDomainEvent(
    Guid BatchId,
    string Code,
    string ReactorId,
    DateTime StartTimeUtc,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}

public sealed record BatchCompletedDomainEvent(
    Guid BatchId,
    string Code,
    string ReactorId,
    DateTime EndTimeUtc,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}

public sealed record BatchAbortedDomainEvent(
    Guid BatchId,
    string Code,
    string ReactorId,
    string Reason,
    DateTime EndTimeUtc,
    DateTime OccurredOnUtc,
    Guid EventId = default
) : IDomainEvent
{
    public Guid EventId { get; init; } = EventId == default ? Guid.NewGuid() : EventId;
}
