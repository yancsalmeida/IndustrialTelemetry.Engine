using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Application;

public class SlidingWindowAnomalyDetectorTests
{
    private readonly Guid _batchId = Guid.NewGuid();

    [Fact]
    public void CalculateRateOfChange_WithSingleSample_ReturnsNull()
    {
        var detector = new SlidingWindowAnomalyDetector();
        var m = TelemetryMeasurement.Create(_batchId, DateTime.UtcNow, "TT-101", 85.0, MeasurementUnit.Celsius);

        var rate = detector.RecordAndCalculateRateOfChange(m, windowSeconds: 60);

        rate.ShouldBeNull();
    }

    [Fact]
    public void CalculateRateOfChange_ThermalRunaway_CalculatesExactRatePerMinute()
    {
        var detector = new SlidingWindowAnomalyDetector();
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        var m1 = TelemetryMeasurement.Create(_batchId, t0, "TT-101", 85.0, MeasurementUnit.Celsius);
        var m2 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(30), "TT-101", 88.0, MeasurementUnit.Celsius);

        detector.RecordAndCalculateRateOfChange(m1, windowSeconds: 60);
        var rate = detector.RecordAndCalculateRateOfChange(m2, windowSeconds: 60);

        rate.ShouldNotBeNull();
        // Delta Y = +3.0 °C, Delta T = 30 seconds (0.5 min) => 6.0 °C/min
        rate.Value.ShouldBe(6.0, 0.001);
    }

    [Fact]
    public void Evaluate_RateOfChangeThermalRunaway_TriggersCriticalAlertAndEnforcesCooldown()
    {
        // Cooldown of 5 seconds to test deduplication
        var detector = new SlidingWindowAnomalyDetector(alertCooldown: TimeSpan.FromSeconds(5));
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        var rule = AlertRule.CreateRateOfChange(
            "TT-101",
            maxRateOfChangePerMinute: 5.0,
            minRateOfChangePerMinute: null,
            AlertSeverity.Critical,
            "Thermal Runaway dT/dt Excursion",
            windowSeconds: 30
        );
        var rules = new[] { rule };

        // 1. Initial baseline point at t0
        var m1 = TelemetryMeasurement.Create(_batchId, t0, "TT-101", 85.0, MeasurementUnit.Celsius);
        var alerts1 = detector.Evaluate(m1, rules);
        alerts1.Count.ShouldBe(0);

        // 2. Steep temperature rise at t0 + 30s: 85°C -> 88°C (dT/dt = +6.0 °C/min > 5.0 °C/min)
        var m2 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(30), "TT-101", 88.0, MeasurementUnit.Celsius);
        var alerts2 = detector.Evaluate(m2, rules);
        alerts2.Count.ShouldBe(1);

        var alert = alerts2[0];
        alert.SensorTag.ShouldBe("TT-101");
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.TriggeredValue.ShouldBe(88.0);
        alert.ThresholdValue.ShouldBe(5.0);
        alert.Message.ShouldContain("RAPID RATE-OF-CHANGE DETECTED on TT-101");
        alert.Message.ShouldContain("+6.00 Celsius/min");

        // 3. Immediately 1 second later (t0 + 31s): rate is still steep, but within 5s cooldown
        var m3 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(31), "TT-101", 88.2, MeasurementUnit.Celsius);
        var alerts3 = detector.Evaluate(m3, rules);
        alerts3.Count.ShouldBe(0); // Suppressed to prevent alarm storming

        // 4. After cooldown expires (t0 + 36s, 6 seconds later): rate still steep => fires fresh alert
        var m4 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(36), "TT-101", 89.2, MeasurementUnit.Celsius);
        var alerts4 = detector.Evaluate(m4, rules);
        alerts4.Count.ShouldBe(1);
    }

    [Fact]
    public void Evaluate_BothStaticThresholdAndRateOfChange_CanTriggerSimultaneously()
    {
        var detector = new SlidingWindowAnomalyDetector(alertCooldown: TimeSpan.Zero);
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        var staticRule = AlertRule.Create("TT-101", minThreshold: 60.0, maxThreshold: 115.0, AlertSeverity.Warning, "High Temp");
        var rocRule = AlertRule.CreateRateOfChange("TT-101", maxRateOfChangePerMinute: 5.0, minRateOfChangePerMinute: null, AlertSeverity.Critical, "Runaway", windowSeconds: 30);
        var rules = new[] { staticRule, rocRule };

        var m1 = TelemetryMeasurement.Create(_batchId, t0, "TT-101", 110.0, MeasurementUnit.Celsius);
        detector.Evaluate(m1, rules);

        // At t0 + 30s, temperature jumps from 110°C to 118°C:
        // Exceeds static limit (118 > 115) AND rate limit ((118 - 110) / 0.5 = 16.0 °C/min > 5.0)
        var m2 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(30), "TT-101", 118.0, MeasurementUnit.Celsius);
        var alerts = detector.Evaluate(m2, rules);

        alerts.Count.ShouldBe(2);
        alerts.ShouldContain(a => a.Severity == AlertSeverity.Warning && a.Message.Contains("HIGH LIMIT EXCEEDED"));
        alerts.ShouldContain(a => a.Severity == AlertSeverity.Critical && a.Message.Contains("RAPID RATE-OF-CHANGE DETECTED"));
    }

    [Fact]
    public void ClearBatch_RemovesSlidingWindowBuffer()
    {
        var detector = new SlidingWindowAnomalyDetector();
        var t0 = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

        var m1 = TelemetryMeasurement.Create(_batchId, t0, "TT-101", 85.0, MeasurementUnit.Celsius);
        detector.RecordAndCalculateRateOfChange(m1);

        detector.ClearBatch(_batchId);

        // Next point should be treated as first sample (rate is null)
        var m2 = TelemetryMeasurement.Create(_batchId, t0.AddSeconds(30), "TT-101", 88.0, MeasurementUnit.Celsius);
        var rate = detector.RecordAndCalculateRateOfChange(m2);

        rate.ShouldBeNull();
    }
}
