using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Infrastructure.Persistence;
using IndustrialTelemetry.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Infrastructure;

public class TelemetryRepositoryBulkCopyTests
{
    [Fact]
    public async Task BulkCopyAsync_InNonPostgresEnvironment_ShouldFallbackToEfCoreAndPersistAllRows()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new IndustrialDbContext(options);
        var repo = new TelemetryRepository(context);

        var batchId = Guid.NewGuid();
        var measurements = Enumerable.Range(1, 25).Select(i =>
            TelemetryMeasurement.Create(
                batchId,
                DateTime.UtcNow.AddSeconds(i),
                $"TT-10{i % 4}",
                75.0 + i,
                MeasurementUnit.Celsius,
                TelemetryQuality.Good
            )
        ).ToList();

        // Act
        var rowsImported = await repo.BulkCopyAsync(measurements);

        // Assert
        rowsImported.ShouldBe(25);

        var persistedCount = await repo.GetCountByBatchAsync(batchId);
        persistedCount.ShouldBe(25);

        var retrieved = await repo.GetByBatchIdAsync(batchId, limit: 100);
        retrieved.Count.ShouldBe(25);
        retrieved.First().BatchId.ShouldBe(batchId);
    }

    [Fact]
    public async Task BulkCopyAsync_WithEmptyList_ShouldReturnZeroImmediately()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        await using var context = new IndustrialDbContext(options);
        var repo = new TelemetryRepository(context);

        // Act
        var rows = await repo.BulkCopyAsync(Array.Empty<TelemetryMeasurement>());

        // Assert
        rows.ShouldBe(0);
    }
}
