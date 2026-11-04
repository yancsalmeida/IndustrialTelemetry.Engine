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
    /// High-throughput ingestion endpoint for time-series sensor measurements with automated threshold alert evaluation.
    /// </summary>
    [HttpPost("ingest")]
    [ProducesResponseType(typeof(IngestResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> IngestTelemetry([FromBody] IngestTelemetryBatchDto dto, CancellationToken ct)
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

        var result = await _ingestionService.IngestAsync(dto, ct);
        return Ok(result);
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
}
