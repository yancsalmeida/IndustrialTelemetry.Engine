using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Domain;

public class AlertRuleTests
{
    [Fact]
    public void Create_WithMinGreaterThanMax_ShouldThrowDomainValidationException()
    {
        Should.Throw<DomainValidationException>(() =>
            AlertRule.Create("TT-101", minThreshold: 100.0, maxThreshold: 80.0, AlertSeverity.Warning, "Invalid"));
    }

    [Fact]
    public void Create_WithoutAnyThreshold_ShouldThrowDomainValidationException()
    {
        Should.Throw<DomainValidationException>(() =>
            AlertRule.Create("TT-101", null, null, AlertSeverity.Warning, "Missing bounds"));
    }

    [Fact]
    public void Evaluate_WhenValueExceedsMax_ShouldTriggerAlert()
    {
        // Arrange
        var rule = AlertRule.Create("TT-101", 50.0, 115.0, AlertSeverity.Critical, "Reactor Overheat");
        var measurement = TelemetryMeasurement.Create(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "TT-101",
            122.5,
            MeasurementUnit.Celsius
        );

        // Act
        var alert = rule.Evaluate(measurement);

        // Assert
        alert.ShouldNotBeNull();
        alert.SensorTag.ShouldBe("TT-101");
        alert.Severity.ShouldBe(AlertSeverity.Critical);
        alert.TriggeredValue.ShouldBe(122.5);
        alert.ThresholdValue.ShouldBe(115.0);
        alert.Message.ShouldContain("HIGH LIMIT EXCEEDED");
    }

    [Fact]
    public void Evaluate_WhenValueIsBelowMin_ShouldTriggerAlert()
    {
        // Arrange
        var rule = AlertRule.Create("PH-101", 6.0, 8.5, AlertSeverity.Warning, "pH Buffer Band");
        var measurement = TelemetryMeasurement.Create(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "PH-101",
            5.2,
            MeasurementUnit.Ph
        );

        // Act
        var alert = rule.Evaluate(measurement);

        // Assert
        alert.ShouldNotBeNull();
        alert.SensorTag.ShouldBe("PH-101");
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.TriggeredValue.ShouldBe(5.2);
        alert.ThresholdValue.ShouldBe(6.0);
        alert.Message.ShouldContain("LOW LIMIT BREACH");
    }

    [Fact]
    public void Evaluate_WhenValueWithinNormalEnvelope_ShouldReturnNull()
    {
        // Arrange
        var rule = AlertRule.Create("PT-101", 1.0, 4.0, AlertSeverity.Warning, "Normal pressure");
        var measurement = TelemetryMeasurement.Create(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "PT-101",
            2.5,
            MeasurementUnit.Bar
        );

        // Act
        var alert = rule.Evaluate(measurement);

        // Assert
        alert.ShouldBeNull();
    }

    [Fact]
    public void Evaluate_WhenRuleIsInactive_ShouldReturnNull()
    {
        // Arrange
        var rule = AlertRule.Create("TT-101", null, 100.0, AlertSeverity.Critical, "Overheat");
        rule.SetActive(false);

        var measurement = TelemetryMeasurement.Create(
            Guid.NewGuid(),
            DateTime.UtcNow,
            "TT-101",
            150.0,
            MeasurementUnit.Celsius
        );

        // Act
        var alert = rule.Evaluate(measurement);

        // Assert
        alert.ShouldBeNull();
    }
}
