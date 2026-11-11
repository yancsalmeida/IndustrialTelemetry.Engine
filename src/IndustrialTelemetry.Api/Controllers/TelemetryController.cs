using System.Text;
using FluentValidation;
using IndustrialTelemetry.Api.Filters;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IndustrialTelemetry.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
[EnableRateLimiting("telemetry-rate-limit")]
public class TelemetryController : ControllerBase
{
    private readonly TelemetryIngestionService _ingestionService;
    private readonly IValidator<SensorMeasurementDto> _measurementValidator;

    public TelemetryController(
        TelemetryIngestionService ingestionService,
        IValidator<SensorMeasurementDto> measurementValidator)
    {
        _ingestionService = ingestionService;
        _measurementValidator = measurementValidator;
    }

    /// <summary>
    /// Transactional ingestion endpoint for time-series sensor measurements with automated threshold alert evaluation.
    /// </summary>
    [HttpPost("ingest")]
    [Idempotent]
    [ProducesResponseType(typeof(IngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestTelemetry([FromBody] IngestTelemetryBatchDto dto, CancellationToken ct)
    {
        var validationError = await ValidateMeasurementsAsync(dto, ct);
        if (validationError != null)
            return validationError;

        var result = await _ingestionService.IngestAsync(dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Asynchronous non-blocking streaming ingestion via bounded in-memory channel.
    /// Fast-paths incoming telemetry into the channel buffer and returns immediately (202 Accepted).
    /// </summary>
    [HttpPost("ingest/channel")]
    [Idempotent]
    [ProducesResponseType(typeof(ChannelIngestResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestChannel([FromBody] IngestTelemetryBatchDto dto, CancellationToken ct)
    {
        var validationError = await ValidateMeasurementsAsync(dto, ct);
        if (validationError != null)
            return validationError;

        var result = await _ingestionService.IngestChannelAsync(dto, ct);
        return Accepted(result);
    }

    /// <summary>
    /// High-throughput direct bulk copy ingestion bypassing EF Core change-tracking overhead
    /// via PostgreSQL Binary COPY.
    /// </summary>
    [HttpPost("ingest/bulk")]
    [Idempotent]
    [ProducesResponseType(typeof(BulkIngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestBulk([FromBody] IngestTelemetryBatchDto dto, CancellationToken ct)
    {
        var validationError = await ValidateMeasurementsAsync(dto, ct);
        if (validationError != null)
            return validationError;

        var result = await _ingestionService.IngestBulkAsync(dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// High-throughput compact wire-protocol telemetry ingestion endpoint for SCADA/PLC edge gateways.
    /// Parses delimiter-separated records (TIMESTAMP|SENSOR_TAG|VALUE|UNIT|QUALITY) with zero intermediate
    /// array allocations and routes measurements directly to the chosen engine mode (Channel, Transactional, or Bulk).
    /// </summary>
    [HttpPost("batch/{batchId:guid}/ingest/wire")]
    [Idempotent]
    [Consumes("text/plain", "application/octet-stream", "application/x-telemetry-wire")]
    [ProducesResponseType(typeof(WireIngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(WireIngestResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestWireProtocol(
        Guid batchId,
        [FromQuery] WireIngestMode mode = WireIngestMode.Channel,
        CancellationToken ct = default)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawText = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Empty Wire Payload",
                Detail = "Wire protocol payload cannot be empty.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _ingestionService.IngestWirePayloadAsync(batchId, rawText, mode, ct);

        if (mode == WireIngestMode.Channel)
        {
            return Accepted(result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Live queue metrics for the in-memory telemetry ingestion channel.
    /// </summary>
    [HttpGet("channel/status")]
    [ProducesResponseType(typeof(ChannelStatusDto), StatusCodes.Status200OK)]
    public IActionResult GetChannelStatus()
    {
        var status = _ingestionService.GetChannelStatus();
        return Ok(status);
    }

    /// <summary>
    /// Retrieves chronological historical sensor records for a specific batch.
    /// </summary>
    [HttpGet("batch/{batchId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<TelemetryMeasurementResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBatchMeasurements(
        Guid batchId,
        [FromQuery] string? sensorTag = null,
        [FromQuery] int limit = 500,
        CancellationToken ct = default)
    {
        var records = await _ingestionService.GetMeasurementsAsync(batchId, sensorTag, limit, ct);
        return Ok(records);
    }

    private async Task<IActionResult?> ValidateMeasurementsAsync(IngestTelemetryBatchDto dto, CancellationToken ct)
    {
        if (dto.Measurements != null)
        {
            foreach (var m in dto.Measurements)
            {
                var val = await _measurementValidator.ValidateAsync(m, ct);
                if (!val.IsValid)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Invalid Sensor Reading",
                        Detail = string.Join("; ", val.Errors.Select(e => e.ErrorMessage)),
                        Status = StatusCodes.Status400BadRequest
                    });
                }
            }
        }
        return null;
    }
}
