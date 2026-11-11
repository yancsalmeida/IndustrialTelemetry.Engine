using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Domain.Entities;

public class AlertRule
{
    public Guid Id { get; private set; }
    public string SensorTag { get; private set; } = string.Empty;
    public double? MinThreshold { get; private set; }
    public double? MaxThreshold { get; private set; }
    public double? MaxRateOfChangePerMinute { get; private set; }
    public double? MinRateOfChangePerMinute { get; private set; }
    public int WindowSeconds { get; private set; } = 60;
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
        string description,
        double? maxRateOfChangePerMinute = null,
        double? minRateOfChangePerMinute = null,
        int windowSeconds = 60)
    {
        if (string.IsNullOrWhiteSpace(sensorTag))
            throw new DomainValidationException("Sensor tag is required for an alert rule.");

        if (!minThreshold.HasValue && !maxThreshold.HasValue &&
            !maxRateOfChangePerMinute.HasValue && !minRateOfChangePerMinute.HasValue)
            throw new DomainValidationException("At least one threshold (Min, Max, or Rate-of-Change) must be specified.");

        if (minThreshold.HasValue && maxThreshold.HasValue && minThreshold.Value >= maxThreshold.Value)
            throw new DomainValidationException("MinThreshold must be strictly less than MaxThreshold.");

        if (minRateOfChangePerMinute.HasValue && maxRateOfChangePerMinute.HasValue && minRateOfChangePerMinute.Value >= maxRateOfChangePerMinute.Value)
            throw new DomainValidationException("MinRateOfChangePerMinute must be strictly less than MaxRateOfChangePerMinute.");

        if (windowSeconds <= 0)
            throw new DomainValidationException("WindowSeconds must be greater than zero.");

        return new AlertRule
        {
            Id = Guid.NewGuid(),
            SensorTag = sensorTag.Trim().ToUpperInvariant(),
            MinThreshold = minThreshold,
            MaxThreshold = maxThreshold,
            MaxRateOfChangePerMinute = maxRateOfChangePerMinute,
            MinRateOfChangePerMinute = minRateOfChangePerMinute,
            WindowSeconds = windowSeconds,
            Severity = severity,
            Description = description?.Trim() ?? string.Empty,
            IsActive = true
        };
    }

    public static AlertRule CreateRateOfChange(
        string sensorTag,
        double? maxRateOfChangePerMinute,
        double? minRateOfChangePerMinute,
        AlertSeverity severity,
        string description,
        int windowSeconds = 60)
    {
        return Create(
            sensorTag,
            minThreshold: null,
            maxThreshold: null,
            severity,
            description,
            maxRateOfChangePerMinute,
            minRateOfChangePerMinute,
            windowSeconds);
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

    public ProcessAlert? EvaluateRateOfChange(TelemetryMeasurement measurement, double rateOfChangePerMinute)
    {
        if (!IsActive)
            return null;

        if (!string.Equals(SensorTag, measurement.SensorTag, StringComparison.OrdinalIgnoreCase))
            return null;

        if (MaxRateOfChangePerMinute.HasValue && rateOfChangePerMinute > MaxRateOfChangePerMinute.Value)
        {
            return ProcessAlert.Create(
                measurement.BatchId,
                SensorTag,
                measurement.TimestampUtc,
                measurement.Value,
                MaxRateOfChangePerMinute.Value,
                Severity,
                $"RAPID RATE-OF-CHANGE DETECTED on {SensorTag}: measured rate of {rateOfChangePerMinute:+0.00;-0.00} {measurement.Unit}/min exceeds limit of {MaxRateOfChangePerMinute.Value:F2} {measurement.Unit}/min (Window: {WindowSeconds}s)."
            );
        }

        if (MinRateOfChangePerMinute.HasValue && rateOfChangePerMinute < MinRateOfChangePerMinute.Value)
        {
            return ProcessAlert.Create(
                measurement.BatchId,
                SensorTag,
                measurement.TimestampUtc,
                measurement.Value,
                MinRateOfChangePerMinute.Value,
                Severity,
                $"RAPID RATE-OF-CHANGE DROP on {SensorTag}: measured rate of {rateOfChangePerMinute:+0.00;-0.00} {measurement.Unit}/min breaches minimum of {MinRateOfChangePerMinute.Value:F2} {measurement.Unit}/min (Window: {WindowSeconds}s)."
            );
        }

        return null;
    }

    public void SetActive(bool active)
    {
        IsActive = active;
    }
}
