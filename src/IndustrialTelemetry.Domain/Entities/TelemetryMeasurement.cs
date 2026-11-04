using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Domain.Entities;

public class TelemetryMeasurement
{
    public long Id { get; private set; }
    public Guid BatchId { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string SensorTag { get; private set; } = string.Empty;
    public double Value { get; private set; }
    public MeasurementUnit Unit { get; private set; }
    public TelemetryQuality Quality { get; private set; }

    // Required by EF Core
    private TelemetryMeasurement() { }

    public static TelemetryMeasurement Create(
        Guid batchId,
        DateTime timestampUtc,
        string sensorTag,
        double value,
        MeasurementUnit unit,
        TelemetryQuality quality = TelemetryQuality.Good)
    {
        if (batchId == Guid.Empty)
            throw new DomainValidationException("Measurement must be associated with a valid BatchId.");

        if (string.IsNullOrWhiteSpace(sensorTag))
            throw new DomainValidationException("Sensor tag is required (e.g., TT-101).");

        return new TelemetryMeasurement
        {
            BatchId = batchId,
            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc ? timestampUtc : timestampUtc.ToUniversalTime(),
            SensorTag = sensorTag.Trim().ToUpperInvariant(),
            Value = value,
            Unit = unit,
            Quality = quality
        };
    }
}
