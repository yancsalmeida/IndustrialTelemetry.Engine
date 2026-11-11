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
    private readonly ITelemetryChannel _channel = Substitute.For<ITelemetryChannel>();
    private readonly TelemetryIngestionService _sut;

    public TelemetryIngestionServiceTests()
    {
        _sut = new TelemetryIngestionService(_batchRepo, _telemetryRepo, _alertRepo, _unitOfWork, _channel);
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

    [Fact]
    public async Task IngestChannelAsync_WhenBatchIsInProgress_ShouldWriteToChannelAndReturnQueuedDepth()
    {
        // Arrange
        var batch = Batch.Create("BATCH-CHAN-1", "Recipe-1", "R-101", 15.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _channel.WriteBatchAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(2));
        _channel.CurrentCount.Returns(42);

        var dto = new IngestTelemetryBatchDto(
            batch.Id,
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "TT-101", 88.5, "Celsius", "Good"),
                new(DateTime.UtcNow, "PT-101", 3.1, "Bar", "Good")
            }
        );

        // Act
        var result = await _sut.IngestChannelAsync(dto);

        // Assert
        result.AcceptedCount.ShouldBe(2);
        result.CurrentQueueDepth.ShouldBe(42);

        await _channel.Received(1).WriteBatchAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(items => items.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestBulkAsync_WhenBatchIsInProgress_ShouldInvokeRepositoryBulkCopyAsync()
    {
        // Arrange
        var batch = Batch.Create("BATCH-BULK-1", "Recipe-1", "R-101", 20.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _telemetryRepo.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(3);

        var dto = new IngestTelemetryBatchDto(
            batch.Id,
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "TT-101", 80.0, "Celsius", "Good"),
                new(DateTime.UtcNow, "TT-102", 82.0, "Celsius", "Good"),
                new(DateTime.UtcNow, "PT-101", 2.5, "Bar", "Good")
            }
        );

        // Act
        var result = await _sut.IngestBulkAsync(dto);

        // Assert
        result.RowsImported.ShouldBe(3);
        result.ElapsedMilliseconds.ShouldBeGreaterThanOrEqualTo(0);

        await _telemetryRepo.Received(1).BulkCopyAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(items => items.Count() == 3),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GetChannelStatus_ShouldReturnChannelLiveStatus()
    {
        // Arrange
        var expectedStatus = new ChannelStatusDto(
            CurrentCount: 15,
            Capacity: 50000,
            TotalIngested: 100,
            TotalDrained: 85,
            TotalDropped: 0,
            IsWriterCompleted: false
        );
        _channel.GetStatus().Returns(expectedStatus);

        // Act
        var status = _sut.GetChannelStatus();

        // Assert
        status.ShouldBe(expectedStatus);
        status.CurrentCount.ShouldBe(15);
        status.Capacity.ShouldBe(50000);
    }

    [Fact]
    public async Task IngestWirePayloadAsync_WhenModeIsChannel_WritesToChannelAndReturnsResult()
    {
        // Arrange
        var batch = Batch.Create("BATCH-WIRE-CHAN", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _channel.WriteBatchAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(2));
        _channel.CurrentCount.Returns(7);

        const string wireText =
            "1730678400000|TT-101|85.0|Celsius|Good\n" +
            "1730678400100|PT-101|3.5|Bar|Good";

        // Act
        var result = await _sut.IngestWirePayloadAsync(batch.Id, wireText, WireIngestMode.Channel);

        // Assert
        result.TotalLinesParsed.ShouldBe(2);
        result.ValidMeasurements.ShouldBe(2);
        result.SkippedLines.ShouldBe(0);
        result.IngestMode.ShouldBe("Channel");
        result.ChannelQueueDepth.ShouldBe(7);

        await _channel.Received(1).WriteBatchAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestWirePayloadAsync_WhenModeIsTransactional_EvaluatesRulesAndPersists()
    {
        // Arrange
        var batch = Batch.Create("BATCH-WIRE-TX", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        var rule = AlertRule.Create(
            "TT-101",
            null,
            90.0,
            AlertSeverity.Critical,
            "Temperature above limit"
        );
        _alertRepo.GetActiveRulesAsync(Arg.Any<CancellationToken>()).Returns(new List<AlertRule> { rule });

        const string wireText =
            "1730678400000|TT-101|95.0|Celsius|Good\n" + // Triggers rule (> 90.0)
            "1730678400100|TT-101|80.0|Celsius|Good";   // Does not trigger rule

        // Act
        var result = await _sut.IngestWirePayloadAsync(batch.Id, wireText, WireIngestMode.Transactional);

        // Assert
        result.ValidMeasurements.ShouldBe(2);
        result.AlertsTriggered.ShouldBe(1);
        result.IngestMode.ShouldBe("Transactional");

        await _telemetryRepo.Received(1).IngestBatchAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 2),
            Arg.Any<CancellationToken>());

        await _alertRepo.Received(1).AddAlertsAsync(
            Arg.Is<IEnumerable<ProcessAlert>>(a => a.Count() == 1),
            Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IngestWirePayloadAsync_WhenModeIsBulk_DelegatesToBulkCopy()
    {
        // Arrange
        var batch = Batch.Create("BATCH-WIRE-BULK", "Recipe-1", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _telemetryRepo.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(3);

        const string wireText =
            "1730678400000|TT-101|80.0|Celsius|Good\n" +
            "1730678400100|TT-102|82.0|Celsius|Good\n" +
            "1730678400200|PT-101|2.5|Bar|Good";

        // Act
        var result = await _sut.IngestWirePayloadAsync(batch.Id, wireText, WireIngestMode.Bulk);

        // Assert
        result.ValidMeasurements.ShouldBe(3);
        result.IngestMode.ShouldBe("Bulk");

        await _telemetryRepo.Received(1).BulkCopyAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 3),
            Arg.Any<CancellationToken>());
    }
}
