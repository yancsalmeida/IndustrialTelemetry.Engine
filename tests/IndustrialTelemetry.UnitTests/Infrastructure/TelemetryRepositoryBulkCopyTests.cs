using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Infrastructure.Persistence;
using IndustrialTelemetry.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public async Task GetSensorAggregatesByBatchAsync_OnRelationalProvider_ShouldExecuteDapperSqlGroupBy()
    {
        // Arrange: Use real relational SQLite in-memory database to exercise Dapper SQL path
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new IndustrialDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var targetBatch = Batch.Create("BATCH-DAPPER-01", "Polymer-A", "R-101", 10.0);
        var otherBatch = Batch.Create("BATCH-DAPPER-02", "Polymer-B", "R-102", 10.0);
        context.Batches.AddRange(targetBatch, otherBatch);

        var t0 = DateTime.UtcNow;
        context.Telemetry.AddRange(
            TelemetryMeasurement.Create(targetBatch.Id, t0, "TT-101", 80.0, MeasurementUnit.Celsius),
            TelemetryMeasurement.Create(targetBatch.Id, t0.AddSeconds(1), "TT-101", 90.0, MeasurementUnit.Celsius),
            TelemetryMeasurement.Create(targetBatch.Id, t0.AddSeconds(2), "TT-101", 100.0, MeasurementUnit.Celsius),
            TelemetryMeasurement.Create(targetBatch.Id, t0, "PT-101", 2.0, MeasurementUnit.Bar),
            TelemetryMeasurement.Create(targetBatch.Id, t0.AddSeconds(1), "PT-101", 4.0, MeasurementUnit.Bar),
            TelemetryMeasurement.Create(otherBatch.Id, t0, "TT-101", 999.0, MeasurementUnit.Celsius)
        );
        await context.SaveChangesAsync();

        var repo = new TelemetryRepository(context);

        // Act
        var aggregates = await repo.GetSensorAggregatesByBatchAsync(targetBatch.Id);
        var count = await repo.GetCountByBatchAsync(targetBatch.Id);

        // Assert
        count.ShouldBe(5);
        aggregates.Count.ShouldBe(2);

        var pt = aggregates.Single(a => a.SensorTag == "PT-101");
        pt.Average.ShouldBe(3.0);
        pt.Min.ShouldBe(2.0);
        pt.Max.ShouldBe(4.0);
        pt.Count.ShouldBe(2);

        var tt = aggregates.Single(a => a.SensorTag == "TT-101");
        tt.Average.ShouldBe(90.0);
        tt.Min.ShouldBe(80.0);
        tt.Max.ShouldBe(100.0);
        tt.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AlertRepository_GetCountsByBatchesAsync_ShouldReturnGroupedCountsPerBatch()
    {
        // Arrange
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var options = new DbContextOptionsBuilder<IndustrialDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var context = new IndustrialDbContext(options);
        await context.Database.EnsureCreatedAsync();

        var batch1 = Batch.Create("BATCH-AL-01", "Recipe-1", "R-101", 5.0);
        var batch2 = Batch.Create("BATCH-AL-02", "Recipe-2", "R-102", 5.0);
        context.Batches.AddRange(batch1, batch2);

        context.ProcessAlerts.AddRange(
            ProcessAlert.Create(batch1.Id, "TT-101", DateTime.UtcNow, 105.0, 100.0, AlertSeverity.Warning, "High temp 1"),
            ProcessAlert.Create(batch1.Id, "PT-101", DateTime.UtcNow, 6.5, 6.0, AlertSeverity.Critical, "High press 1")
        );
        await context.SaveChangesAsync();

        var alertRepo = new AlertRepository(context);

        // Act
        var counts = await alertRepo.GetCountsByBatchesAsync(new[] { batch1.Id, batch2.Id });

        // Assert
        counts.ContainsKey(batch1.Id).ShouldBeTrue();
        counts[batch1.Id].ShouldBe(2);
        counts.ContainsKey(batch2.Id).ShouldBeFalse();
    }
}
