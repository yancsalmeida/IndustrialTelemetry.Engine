using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Infrastructure.BackgroundServices;
using IndustrialTelemetry.Infrastructure.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Infrastructure;

public class TelemetryChannelConsumerServiceTests
{
    private readonly ITelemetryRepository _repository = Substitute.For<ITelemetryRepository>();
    private readonly IServiceScopeFactory _scopeFactory = Substitute.For<IServiceScopeFactory>();
    private readonly IServiceScope _scope = Substitute.For<IServiceScope>();
    private readonly IServiceProvider _serviceProvider = Substitute.For<IServiceProvider>();

    public TelemetryChannelConsumerServiceTests()
    {
        _scopeFactory.CreateScope().Returns(_scope);
        _scope.ServiceProvider.Returns(_serviceProvider);
        _serviceProvider.GetService(typeof(ITelemetryRepository)).Returns(_repository);
    }

    [Fact]
    public async Task Consumer_ShouldDrainChannelAndFlushBatchesToRepository()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 100);
        var options = Options.Create(new TelemetryChannelOptions
        {
            Capacity = 100,
            BatchSize = 3,
            FlushIntervalMs = 20
        });

        var sut = new TelemetryChannelConsumerService(
            channel,
            _scopeFactory,
            NullLogger<TelemetryChannelConsumerService>.Instance,
            options
        );

        var batchId = Guid.NewGuid();
        var measurements = Enumerable.Range(1, 3).Select(i =>
            TelemetryMeasurement.Create(batchId, DateTime.UtcNow, "TT-101", 60.0 + i, MeasurementUnit.Celsius)
        ).ToList();

        await channel.WriteBatchAsync(measurements);

        _repository.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(3);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        var runTask = sut.StartAsync(cts.Token);
        // Wait briefly for batch processing
        await Task.Delay(100);
        await sut.StopAsync(CancellationToken.None);
        await runTask;

        // Assert
        await _repository.Received().BulkCopyAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 3),
            Arg.Any<CancellationToken>()
        );
        channel.CurrentCount.ShouldBe(0);
    }

    [Fact]
    public async Task Consumer_WhenIncompleteBatchReachesFlushTimeout_ShouldFlushPartialBatch()
    {
        // Arrange
        var channel = new TelemetryChannel(capacity: 100);
        var options = Options.Create(new TelemetryChannelOptions
        {
            Capacity = 100,
            BatchSize = 10, // larger than available items
            FlushIntervalMs = 25
        });

        var sut = new TelemetryChannelConsumerService(
            channel,
            _scopeFactory,
            NullLogger<TelemetryChannelConsumerService>.Instance,
            options
        );

        var batchId = Guid.NewGuid();
        var m1 = TelemetryMeasurement.Create(batchId, DateTime.UtcNow, "PT-101", 2.5, MeasurementUnit.Bar);
        await channel.WriteAsync(m1);

        _repository.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(1);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        var runTask = sut.StartAsync(cts.Token);
        await Task.Delay(120); // allow flush interval (25ms) to elapse
        await sut.StopAsync(CancellationToken.None);
        await runTask;

        // Assert
        await _repository.Received().BulkCopyAsync(
            Arg.Is<IEnumerable<TelemetryMeasurement>>(m => m.Count() == 1),
            Arg.Any<CancellationToken>()
        );
        channel.CurrentCount.ShouldBe(0);
    }
}
