using FluentValidation;
using FluentValidation.Results;
using IndustrialTelemetry.Api.Controllers;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Shouldly;
using Xunit;

namespace IndustrialTelemetry.UnitTests.Api;

public class TelemetryControllerTests
{
    private readonly IBatchRepository _batchRepo = Substitute.For<IBatchRepository>();
    private readonly ITelemetryRepository _telemetryRepo = Substitute.For<ITelemetryRepository>();
    private readonly IAlertRepository _alertRepo = Substitute.For<IAlertRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ITelemetryChannel _channel = Substitute.For<ITelemetryChannel>();
    private readonly IValidator<SensorMeasurementDto> _validator = Substitute.For<IValidator<SensorMeasurementDto>>();
    private readonly TelemetryIngestionService _service;
    private readonly TelemetryController _controller;

    public TelemetryControllerTests()
    {
        _service = new TelemetryIngestionService(_batchRepo, _telemetryRepo, _alertRepo, _unitOfWork, _channel);
        _controller = new TelemetryController(_service, _validator);

        // Default validator passing
        _validator.ValidateAsync(Arg.Any<SensorMeasurementDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
    }

    [Fact]
    public async Task IngestChannel_WithValidPayload_ShouldReturn202Accepted()
    {
        // Arrange
        var batch = Batch.Create("BATCH-CHAN-API", "Recipe-X", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _channel.WriteBatchAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(1));
        _channel.CurrentCount.Returns(5);

        var dto = new IngestTelemetryBatchDto(
            batch.Id,
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "TT-101", 85.0, "Celsius", "Good")
            }
        );

        // Act
        var result = await _controller.IngestChannel(dto, CancellationToken.None);

        // Assert
        var acceptedResult = result.ShouldBeOfType<AcceptedResult>();
        acceptedResult.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        var body = acceptedResult.Value.ShouldBeOfType<ChannelIngestResultDto>();
        body.AcceptedCount.ShouldBe(1);
        body.CurrentQueueDepth.ShouldBe(5);
    }

    [Fact]
    public async Task IngestBulk_WithValidPayload_ShouldReturn200Ok()
    {
        // Arrange
        var batch = Batch.Create("BATCH-BULK-API", "Recipe-X", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _telemetryRepo.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(5);

        var dto = new IngestTelemetryBatchDto(
            batch.Id,
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "TT-101", 85.0, "Celsius", "Good")
            }
        );

        // Act
        var result = await _controller.IngestBulk(dto, CancellationToken.None);

        // Assert
        var okResult = result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var body = okResult.Value.ShouldBeOfType<BulkIngestResultDto>();
        body.RowsImported.ShouldBe(5);
    }

    [Fact]
    public void GetChannelStatus_ShouldReturn200OkWithChannelStatus()
    {
        // Arrange
        var statusDto = new ChannelStatusDto(
            CurrentCount: 12,
            Capacity: 50000,
            TotalIngested: 250,
            TotalDrained: 238,
            TotalDropped: 0,
            IsWriterCompleted: false
        );
        _channel.GetStatus().Returns(statusDto);

        // Act
        var result = _controller.GetChannelStatus();

        // Assert
        var okResult = result.ShouldBeOfType<OkObjectResult>();
        var body = okResult.Value.ShouldBeOfType<ChannelStatusDto>();
        body.CurrentCount.ShouldBe(12);
        body.Capacity.ShouldBe(50000);
        body.TotalIngested.ShouldBe(250);
    }

    [Fact]
    public async Task IngestChannel_WhenMeasurementValidationFails_ShouldReturn400BadRequest()
    {
        // Arrange
        _validator.ValidateAsync(Arg.Any<SensorMeasurementDto>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult(new[] { new ValidationFailure("SensorTag", "SensorTag is required") }));

        var dto = new IngestTelemetryBatchDto(
            Guid.NewGuid(),
            new List<SensorMeasurementDto>
            {
                new(DateTime.UtcNow, "", 85.0, "Celsius", "Good")
            }
        );

        // Act
        var result = await _controller.IngestChannel(dto, CancellationToken.None);

        // Assert
        var badRequest = result.ShouldBeOfType<BadRequestObjectResult>();
        badRequest.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task IngestWireProtocol_WithChannelMode_ShouldReturn202Accepted()
    {
        // Arrange
        var batch = Batch.Create("BATCH-WIRE-API", "Recipe-X", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _channel.WriteBatchAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(2));
        _channel.CurrentCount.Returns(4);

        const string wirePayload = "1730678400000|TT-101|85.0|Celsius|Good\n1730678400100|PT-101|3.5|Bar|Good";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wirePayload));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await _controller.IngestWireProtocol(batch.Id, WireIngestMode.Channel, CancellationToken.None);

        // Assert
        var acceptedResult = result.ShouldBeOfType<AcceptedResult>();
        acceptedResult.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        var body = acceptedResult.Value.ShouldBeOfType<WireIngestResultDto>();
        body.ValidMeasurements.ShouldBe(2);
        body.ChannelQueueDepth.ShouldBe(4);
    }

    [Fact]
    public async Task IngestWireProtocol_WithBulkMode_ShouldReturn200Ok()
    {
        // Arrange
        var batch = Batch.Create("BATCH-WIRE-API-BULK", "Recipe-X", "R-101", 10.0);
        batch.Start(DateTime.UtcNow);
        _batchRepo.GetByIdAsync(batch.Id, Arg.Any<CancellationToken>()).Returns(batch);

        _telemetryRepo.BulkCopyAsync(Arg.Any<IEnumerable<TelemetryMeasurement>>(), Arg.Any<CancellationToken>())
            .Returns(2);

        const string wirePayload = "1730678400000|TT-101|85.0|Celsius|Good\n1730678400100|PT-101|3.5|Bar|Good";
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(wirePayload));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await _controller.IngestWireProtocol(batch.Id, WireIngestMode.Bulk, CancellationToken.None);

        // Assert
        var okResult = result.ShouldBeOfType<OkObjectResult>();
        okResult.StatusCode.ShouldBe(StatusCodes.Status200OK);
        var body = okResult.Value.ShouldBeOfType<WireIngestResultDto>();
        body.ValidMeasurements.ShouldBe(2);
        body.IngestMode.ShouldBe("Bulk");
    }

    [Fact]
    public async Task IngestWireProtocol_WithEmptyPayload_ShouldReturn400BadRequest()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("   "));
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        // Act
        var result = await _controller.IngestWireProtocol(Guid.NewGuid(), WireIngestMode.Channel, CancellationToken.None);

        // Assert
        var badRequest = result.ShouldBeOfType<BadRequestObjectResult>();
        badRequest.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
    }
}
