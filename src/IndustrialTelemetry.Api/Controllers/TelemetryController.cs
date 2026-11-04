using FluentValidation;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialTelemetry.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
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
