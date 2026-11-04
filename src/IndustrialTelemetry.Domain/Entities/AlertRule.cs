using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Domain.Entities;

public class AlertRule
{
    public Guid Id { get; private set; }
    public string SensorTag { get; private set; } = string.Empty;
    public double? MinThreshold { get; private set; }
    public double? MaxThreshold { get; private set; }
    public AlertSeverity Severity { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }

    // Required by EF Core
    private AlertRule() { }

    public static AlertRule Create(
        string sensorTag,
        double? minThreshold,
        double? maxThreshold,
        AlertSeverity severity,
        string description)
    {
        if (string.IsNullOrWhiteSpace(sensorTag))
            throw new DomainValidationException("Sensor tag is required for an alert rule.");

        if (!minThreshold.HasValue && !maxThreshold.HasValue)
            throw new DomainValidationException("At least one threshold (Min or Max) must be specified.");

        if (minThreshold.HasValue && maxThreshold.HasValue && minThreshold.Value >= maxThreshold.Value)
            throw new DomainValidationException("MinThreshold must be strictly less than MaxThreshold.");

        return new AlertRule
        {
            Id = Guid.NewGuid(),
            SensorTag = sensorTag.Trim().ToUpperInvariant(),
            MinThreshold = minThreshold,
            MaxThreshold = maxThreshold,
            Severity = severity,
            Description = description?.Trim() ?? string.Empty,
            IsActive = true
        };
    }

    public ProcessAlert? Evaluate(TelemetryMeasurement measurement)
    {
        if (!IsActive)
            return null;

        if (!string.Equals(SensorTag, measurement.SensorTag, StringComparison.OrdinalIgnoreCase))
            return null;

        if (MaxThreshold.HasValue && measurement.Value > MaxThreshold.Value)
        {
            return ProcessAlert.Create(
                measurement.BatchId,
                SensorTag,
                measurement.TimestampUtc,
                measurement.Value,
                MaxThreshold.Value,
                Severity,
                $"HIGH LIMIT EXCEEDED on {SensorTag}: measured {measurement.Value:F2} breaches limit of {MaxThreshold.Value:F2} {measurement.Unit}."
            );
        }

        if (MinThreshold.HasValue && measurement.Value < MinThreshold.Value)
        {
            return ProcessAlert.Create(
                measurement.BatchId,
                SensorTag,
                measurement.TimestampUtc,
                measurement.Value,
                MinThreshold.Value,
                Severity,
                $"LOW LIMIT BREACH on {SensorTag}: measured {measurement.Value:F2} is below minimum threshold of {MinThreshold.Value:F2} {measurement.Unit}."
            );
        }

        return null;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
    }
}
