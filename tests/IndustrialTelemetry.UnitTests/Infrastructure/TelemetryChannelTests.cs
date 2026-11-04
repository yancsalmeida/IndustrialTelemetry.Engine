using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Infrastructure.Channels;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Infrastructure;

public class TelemetryChannelTests
{
    [Fact]
    public async Task WriteAsync_And_TryRead_ShouldProcessItemSuccessfully()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 100);
        var batchId = Guid.NewGuid();
        var measurement = TelemetryMeasurement.Create(
            batchId,
            DateTime.UtcNow,
            "TT-101",
            85.5,
            MeasurementUnit.Celsius
        );

        // Act
        var written = await channel.WriteAsync(measurement);
        var readSuccess = channel.TryRead(out var readItem);

        // Assert
        written.ShouldBeTrue();
        readSuccess.ShouldBeTrue();
        readItem.ShouldNotBeNull();
        readItem.SensorTag.ShouldBe("TT-101");
        readItem.Value.ShouldBe(85.5);

        var status = channel.GetStatus();
        status.TotalIngested.ShouldBe(1);
        status.TotalDrained.ShouldBe(1);
        status.CurrentCount.ShouldBe(0);
    }

    [Fact]
    public async Task WriteBatchAsync_ShouldWriteAllMeasurementsAndTrackMetrics()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 500);
        var batchId = Guid.NewGuid();
        var measurements = Enumerable.Range(1, 10).Select(i =>
            TelemetryMeasurement.Create(
                batchId,
                DateTime.UtcNow.AddSeconds(i),
                $"TT-10{i % 3}",
                50.0 + i,
                MeasurementUnit.Celsius
            )
        ).ToList();

        // Act
        var writtenCount = await channel.WriteBatchAsync(measurements);

        // Assert
        writtenCount.ShouldBe(10);
        channel.CurrentCount.ShouldBe(10);

        var status = channel.GetStatus();
        status.TotalIngested.ShouldBe(10);
        status.TotalDrained.ShouldBe(0);
        status.Capacity.ShouldBe(500);

        // Drain 5 items
        for (int i = 0; i < 5; i++)
        {
            channel.TryRead(out _).ShouldBeTrue();
        }

        channel.CurrentCount.ShouldBe(5);
        status = channel.GetStatus();
        status.TotalDrained.ShouldBe(5);
    }

    [Fact]
    public void TryWrite_WhenChannelHasCapacity_ShouldSucceed()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 5);
        var batchId = Guid.NewGuid();
        var measurement = TelemetryMeasurement.Create(batchId, DateTime.UtcNow, "PT-101", 3.2, MeasurementUnit.Bar);

        // Act
        var success = channel.TryWrite(measurement);

        // Assert
        success.ShouldBeTrue();
        channel.CurrentCount.ShouldBe(1);
        channel.GetStatus().TotalIngested.ShouldBe(1);
    }

    [Fact]
    public void CompleteWriter_ShouldMarkWriterAsCompleted()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 10);

        // Act
        channel.CompleteWriter();

        // Assert
        var status = channel.GetStatus();
        status.IsWriterCompleted.ShouldBeTrue();
    }
}
