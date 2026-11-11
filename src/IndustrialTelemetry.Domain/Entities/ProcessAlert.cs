using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Events;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Domain.Entities;

public class ProcessAlert : IHasDomainEvents
{
    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public string SensorTag { get; private set; } = string.Empty;
    public DateTime TimestampUtc { get; private set; }
    public double TriggeredValue { get; private set; }
    public double ThresholdValue { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public bool Acknowledged { get; private set; }
    public DateTime? AcknowledgedAtUtc { get; private set; }
    public string? AcknowledgedBy { get; private set; }

    // Domain events collection for transactional outbox
    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void AddDomainEvent(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    // Required by EF Core
    private ProcessAlert() { }

    public static ProcessAlert Create(
        Guid batchId,
        string sensorTag,
        DateTime timestampUtc,
        double triggeredValue,
        double thresholdValue,
        AlertSeverity severity,
        string message)
    {
        if (batchId == Guid.Empty)
            throw new DomainValidationException("Alert must be linked to a valid BatchId.");

        if (string.IsNullOrWhiteSpace(sensorTag))
            throw new DomainValidationException("Sensor tag is required for alert annunciation.");

        if (string.IsNullOrWhiteSpace(message))
            throw new DomainValidationException("Alert message cannot be blank.");

        var alert = new ProcessAlert
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            SensorTag = sensorTag.Trim().ToUpperInvariant(),
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime(),
            TriggeredValue = triggeredValue,
            ThresholdValue = thresholdValue,
            Severity = severity,
            Message = message.Trim(),
            Acknowledged = false
        };

        alert.AddDomainEvent(new ProcessAlertTriggeredDomainEvent(
            alert.Id,
            alert.BatchId,
            alert.SensorTag,
            alert.TimestampUtc,
            alert.TriggeredValue,
            alert.ThresholdValue,
            alert.Severity,
            alert.Message,
            DateTime.UtcNow));

        return alert;
    }

    public void Acknowledge(string operatorId, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(operatorId))
            throw new DomainValidationException("Operator identification is required to acknowledge an alert.");

        if (Acknowledged)
            return; // Idempotent acknowledgment

        Acknowledged = true;
        AcknowledgedAtUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime();
        AcknowledgedBy = operatorId.Trim();

        AddDomainEvent(new ProcessAlertAcknowledgedDomainEvent(
            Id,
            BatchId,
            AcknowledgedBy,
            AcknowledgedAtUtc.Value,
            DateTime.UtcNow));
    }
}
