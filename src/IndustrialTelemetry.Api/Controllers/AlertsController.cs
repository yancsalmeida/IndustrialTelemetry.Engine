using FluentValidation;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialTelemetry.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class AlertsController : ControllerBase
{
    private readonly AlertService _alertService;
    private readonly IValidator<CreateAlertRuleDto> _ruleValidator;

    public AlertsController(AlertService alertService, IValidator<CreateAlertRuleDto> ruleValidator)
    {
        _alertService = alertService;
        _ruleValidator = ruleValidator;
    }

    /// <summary>
    /// Lists all registered industrial safety and process excursion alert rules.
    /// </summary>
    [HttpGet("rules")]
    [ProducesResponseType(typeof(IReadOnlyList<AlertRuleResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRules(CancellationToken ct)
    {
        var rules = await _alertService.ListRulesAsync(ct);
        return Ok(rules);
    }

    /// <summary>
    /// Registers a new automated alert rule for a process sensor tag.
    /// </summary>
    [HttpPost("rules")]
    [ProducesResponseType(typeof(AlertRuleResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRule([FromBody] CreateAlertRuleDto dto, CancellationToken ct)
    {
        var validation = await _ruleValidator.ValidateAsync(dto, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Failed",
                Detail = string.Join("; ", validation.Errors.Select(e => e.ErrorMessage)),
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _alertService.CreateRuleAsync(dto, ct);
        return CreatedAtAction(nameof(ListRules), result);
    }

    /// <summary>
    /// Retrieves all triggered process and safety alerts associated with a specific batch.
    /// </summary>
    [HttpGet("batch/{batchId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<ProcessAlertResponseDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAlertsForBatch(Guid batchId, CancellationToken ct)
    {
        var alerts = await _alertService.GetAlertsForBatchAsync(batchId, ct);
        return Ok(alerts);
    }

    /// <summary>
    /// Acknowledges an active process alarm with operator ID for audit trail.
    /// </summary>
    [HttpPost("{id:guid}/acknowledge")]
    [ProducesResponseType(typeof(ProcessAlertResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeAlert(Guid id, [FromBody] AcknowledgeAlertDto dto, CancellationToken ct)
    {
        var result = await _alertService.AcknowledgeAlertAsync(id, dto, ct);
        return Ok(result);
    }
}
