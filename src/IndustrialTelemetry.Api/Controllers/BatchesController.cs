using FluentValidation;
using IndustrialTelemetry.Api.Filters;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Services;
using IndustrialTelemetry.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialTelemetry.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class BatchesController : ControllerBase
{
    private readonly BatchService _batchService;
    private readonly IValidator<CreateBatchDto> _createValidator;

    public BatchesController(BatchService batchService, IValidator<CreateBatchDto> createValidator)
    {
        _batchService = batchService;
        _createValidator = createValidator;
    }

    /// <summary>
    /// Creates and schedules a new industrial manufacturing batch.
    /// </summary>
    [HttpPost]
    [Idempotent]
    [ProducesResponseType(typeof(BatchResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateBatch([FromBody] CreateBatchDto dto, CancellationToken ct)
    {
        var validationResult = await _createValidator.ValidateAsync(dto, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Failed",
                Detail = string.Join("; ", validationResult.Errors.Select(e => e.ErrorMessage)),
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _batchService.CreateBatchAsync(dto, ct);
        return CreatedAtAction(nameof(GetBatchById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Retrieves a paginated list of manufacturing batches with optional status filtering.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<BatchResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListBatches([FromQuery] BatchStatus? status = null, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var results = await _batchService.ListBatchesAsync(status, limit, ct);
        return Ok(results);
    }

    /// <summary>
    /// Retrieves full details of a specific batch by its unique identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BatchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatchById(Guid id, CancellationToken ct)
    {
        var result = await _batchService.GetBatchByIdAsync(id, ct);
        return Ok(result);
    }

    /// <summary>
    /// Transitions a scheduled batch into the InProgress execution phase.
    /// </summary>
    [HttpPost("{id:guid}/start")]
    [Idempotent]
    [ProducesResponseType(typeof(BatchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartBatch(Guid id, [FromBody] StartBatchDto? dto, CancellationToken ct)
    {
        var result = await _batchService.StartBatchAsync(id, dto ?? new StartBatchDto(null), ct);
        return Ok(result);
    }

    /// <summary>
    /// Formally completes an InProgress batch run and seals execution timestamps.
    /// </summary>
    [HttpPost("{id:guid}/complete")]
    [Idempotent]
    [ProducesResponseType(typeof(BatchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CompleteBatch(Guid id, [FromBody] CompleteBatchDto? dto, CancellationToken ct)
    {
        var result = await _batchService.CompleteBatchAsync(id, dto ?? new CompleteBatchDto(null), ct);
        return Ok(result);
    }

    /// <summary>
    /// Emergency aborts an active batch with a mandatory regulatory compliance explanation.
    /// </summary>
    [HttpPost("{id:guid}/abort")]
    [Idempotent]
    [ProducesResponseType(typeof(BatchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AbortBatch(Guid id, [FromBody] AbortBatchDto dto, CancellationToken ct)
    {
        var result = await _batchService.AbortBatchAsync(id, dto, ct);
        return Ok(result);
    }

    /// <summary>
    /// Generates key industrial analytics and telemetry statistics for a batch.
    /// </summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(BatchSummaryAnalyticsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBatchSummary(Guid id, CancellationToken ct)
    {
        var summary = await _batchService.GetBatchSummaryAsync(id, ct);
        return Ok(summary);
    }
}
