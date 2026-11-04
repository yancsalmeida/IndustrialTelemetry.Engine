using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Application;

public class TelemetryIngestionServiceTests
{
    private readonly IBatchRepository _batchRepo = Substitute.For<IBatchRepository>();
    private readonly ITelemetryRepository _telemetryRepo = Substitute.For<ITelemetryRepository>();
    private readonly IAlertRepository _alertRepo = Substitute.For<IAlertRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly TelemetryIngestionService _sut;

    public TelemetryIngestionServiceTests()
    {
        _sut = new TelemetryIngestionService(_batchRepo, _telemetryRepo, _alertRepo, _unitOfWork);
    }

    [Fact]
    public async Task IngestAsync_WhenBatchIsScheduledNotYetStarted_ShouldThrowDomainValidationException()
    {
        // Arrange
        var batch = Batch.Create("BATCH-SCHED", "Recipe-1", "R-101", 10.0);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        var dto = new IngestTelemetryBatchDto(
            batch.Id,
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "TT-101", 85.0, "Celsius", "Good")
            }
        );

        // Act & Assert
        await Should.ThrowAsync<DomainValidationException>(async () =>
            await _sut.IngestAsync(dto));

        await _telemetryRepo.DidNotReceive().IngestBatchAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestAsync_WithBreachingValues_ShouldPersistMeasurementsAndTriggerAlerts()
    {
        // Arrange
        var batch = Batch.Create("BATCH-RUNNING", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        var rules = new List<AlertRule>
        {
            AlertRule.Create("TT-101", minThreshold: 60.0, maxThreshold: 110.0, AlertSeverity.Critical, "Overheat Limit")
        };
        _alertRepo.GetActiveRulesAsync(Arg.Any<CancellationToken>())
            .Returns(rules);

        var measurements = new List<SensorMeasurementDto>
        {
            new(DateTime.UtcNow, "TT-101", 85.0, "Celsius", "Good"),     // normal
            new(DateTime.UtcNow, "TT-101", 125.4, "Celsius", "Good"),    // breaches max (110.0) -> Alert!
            new(DateTime.UtcNow, "PT-101", 2.2, "Bar", "Good")          // normal, no matching rule
        };

        var dto = new IngestTelemetryBatchDto(batch.Id, measurements);

        // Act
        var result = await _sut.IngestAsync(dto);

        // Assert
        result.MeasurementsIngested.ShouldBe(3);
        result.AlertsTriggered.ShouldBe(1);

        await _telemetryRepo.Received(1).IngestBatchAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 3),
            Arg.Any<CancellationToken>());

        await _alertRepo.Received(1).AddAlertsAsync(
            Arg.Is<IEnumerable<ProcessAlert>>(a => a.Count() == 1 && a.First().SensorTag == "TT-101"),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
