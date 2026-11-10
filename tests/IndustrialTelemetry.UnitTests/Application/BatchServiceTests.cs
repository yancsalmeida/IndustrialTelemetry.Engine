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

public class BatchServiceTests
{
    private readonly IBatchRepository _batchRepo = Substitute.For<IBatchRepository>();
    private readonly ITelemetryRepository _telemetryRepo = Substitute.For<ITelemetryRepository>();
    private readonly IAlertRepository _alertRepo = Substitute.For<IAlertRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly BatchService _sut;

    public BatchServiceTests()
    {
        _sut = new BatchService(_batchRepo, _telemetryRepo, _alertRepo, _unitOfWork);
    }

    [Fact]
    public async Task CreateBatchAsync_WhenCodeAlreadyExists_ShouldThrowDomainValidationException()
    {
        // Arrange
        var existing = Batch.Create("BATCH-EXISTING", "Recipe-A", "R-101", 10.0);
        _batchRepo.GetByCodeAsync("BATCH-EXISTING", Arg.Any<CancellationToken>())
            .Returns(existing);

        var dto = new CreateBatchDto("BATCH-EXISTING", "Recipe-B", "R-102", 15.0, null);

        // Act & Assert
        await Should.ThrowAsync<DomainValidationException>(async () =>
            await _sut.CreateBatchAsync(dto));

        await _batchRepo.DidNotReceive().AddAsync(Arg.Any<Batch>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateBatchAsync_WhenValid_ShouldPersistAndReturnCreatedBatch()
    {
        // Arrange
        _batchRepo.GetByCodeAsync("BATCH-NEW", Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        var dto = new CreateBatchDto("BATCH-NEW", "Recipe-A", "R-101", 10.0, "Test Notes");

        // Act
        var result = await _sut.CreateBatchAsync(dto);

        // Assert
        result.Code.ShouldBe("BATCH-NEW");
        result.Status.ShouldBe(BatchStatus.Scheduled.ToString());
        await _batchRepo.Received(1).AddAsync(Arg.Any<Batch>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartBatchAsync_WhenNotFound_ShouldThrowEntityNotFoundException()
    {
        var missingId = Guid.NewGuid();
        _batchRepo.GetByIdAsync(missingId, Arg.Any<CancellationToken>())
            .Returns((Batch?)null);

        await Should.ThrowAsync<EntityNotFoundException>(async () =>
            await _sut.StartBatchAsync(missingId, new StartBatchDto(null)));
    }

    [Fact]
    public async Task GetBatchSummaryAsync_ShouldCalculateDurationsAndAggregates()
    {
        // Arrange
        var batch = Batch.Create("BATCH-KPI", "Recipe-X", "R-101", 20.0);
        var start = DateTime.UtcNow.AddHours(-1);
        batch.Start(start);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(batch);

        var aggregates = new List<SensorAggregate>
        {
            new("TT-101", Average: 86.4, Min: 70.0, Max: 98.2, Count: 120),
            new("PT-101", Average: 1.8, Min: 1.0, Max: 3.2, Count: 120)
        };
        _telemetryRepo.GetSensorAggregatesByBatchAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(aggregates);
        _telemetryRepo.GetCountByBatchAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(240);
        _alertRepo.GetAlertsByBatchIdAsync(batch.Id, Arg.Any<CancellationToken>())
            .Returns(new List<ProcessAlert>());

        // Act
        var summary = await _sut.GetBatchSummaryAsync(batch.Id);

        // Assert
        summary.BatchCode.ShouldBe("BATCH-KPI");
        summary.Duration.ShouldNotBeNull();
        summary.AvgTemperatureC.ShouldBe(86.4);
        summary.MaxTemperatureC.ShouldBe(98.2);
        summary.MaxPressureBar.ShouldBe(3.2);
        summary.TotalMeasurements.ShouldBe(240);
        summary.TotalAlerts.ShouldBe(0);
    }

    [Fact]
    public async Task ListBatchesAsync_ShouldFetchAlertCountsInSingleBatchedQuery_AvoidingNPlusOne()
    {
        // Arrange
        var batch1 = Batch.Create("BATCH-001", "Recipe-A", "R-101", 10.0);
        var batch2 = Batch.Create("BATCH-002", "Recipe-B", "R-102", 15.0);
        var batch3 = Batch.Create("BATCH-003", "Recipe-C", "R-103", 20.0);

        _batchRepo.ListAsync(null, 50, Arg.Any<CancellationToken>())
            .Returns(new List<Batch> { batch1, batch2, batch3 });

        var counts = new Dictionary<Guid, int>
        {
            [batch1.Id] = 3,
            [batch3.Id] = 1
        };

        _alertRepo.GetCountsByBatchesAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(counts);

        // Act
        var results = await _sut.ListBatchesAsync(null, 50);

        // Assert
        results.Count.ShouldBe(3);
        results[0].AlertCount.ShouldBe(3);
        results[1].AlertCount.ShouldBe(0);
        results[2].AlertCount.ShouldBe(1);

        await _alertRepo.Received(1).GetCountsByBatchesAsync(
            Arg.Is<IEnumerable<Guid>>(ids => ids.Count() == 3),
            Arg.Any<CancellationToken>());
        await _alertRepo.DidNotReceive().GetCountByBatchAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }
}
