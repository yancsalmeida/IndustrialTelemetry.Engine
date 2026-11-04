using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Application.Services;

public class AlertService
{
    private readonly IAlertRepository _alertRepo;
    private readonly IUnitOfWork _unitOfWork;

    public AlertService(IAlertRepository alertRepo, IUnitOfWork unitOfWork)
    {
        _alertRepo = alertRepo;
        _unitOfWork = unitOfWork;
    }

    public async Task<AlertRuleResponseDto> CreateRuleAsync(CreateAlertRuleDto dto, CancellationToken ct = default)
    {
        if (!Enum.TryParse<AlertSeverity>(dto.Severity, true, out var severity))
        {
            severity = AlertSeverity.Warning;
        }

        var rule = AlertRule.Create(
            dto.SensorTag,
            dto.MinThreshold,
            dto.MaxThreshold,
            severity,
            dto.Description
        );

        await _alertRepo.AddRuleAsync(rule, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return new AlertRuleResponseDto(
            rule.Id,
            rule.SensorTag,
            rule.MinThreshold,
            rule.MaxThreshold,
            rule.Severity.ToString(),
            rule.Description,
            rule.IsActive
        );
    }

    public async Task<IReadOnlyList<AlertRuleResponseDto>> ListRulesAsync(CancellationToken ct = default)
    {
        var rules = await _alertRepo.ListAllRulesAsync(ct);
        return rules.Select(r => new AlertRuleResponseDto(
            r.Id,
            r.SensorTag,
            r.MinThreshold,
            r.MaxThreshold,
            r.Severity.ToString(),
            r.Description,
            r.IsActive
        )).ToList();
    }

    public async Task<IReadOnlyList<ProcessAlertResponseDto>> GetAlertsForBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        var alerts = await _alertRepo.GetAlertsByBatchIdAsync(batchId, ct);
        return alerts.Select(a => new ProcessAlertResponseDto(
            a.Id,
            a.BatchId,
            a.SensorTag,
            a.TimestampUtc,
            a.TriggeredValue,
            a.ThresholdValue,
            a.Severity.ToString(),
            a.Message,
            a.Acknowledged,
            a.AcknowledgedAtUtc,
            a.AcknowledgedBy
        )).ToList();
    }

    public async Task<ProcessAlertResponseDto> AcknowledgeAlertAsync(Guid alertId, AcknowledgeAlertDto dto, CancellationToken ct = default)
    {
        var alert = await _alertRepo.GetAlertByIdAsync(alertId, ct)
            ?? throw new EntityNotFoundException(nameof(ProcessAlert), alertId);

        var timestamp = dto.TimestampUtc ?? DateTime.UtcNow;
        alert.Acknowledge(dto.OperatorId, timestamp);

        await _unitOfWork.SaveChangesAsync(ct);

        return new ProcessAlertResponseDto(
            alert.Id,
            alert.BatchId,
            alert.SensorTag,
            alert.TimestampUtc,
            alert.TriggeredValue,
            alert.ThresholdValue,
            alert.Severity.ToString(),
            alert.Message,
            alert.Acknowledged,
            alert.AcknowledgedAtUtc,
            alert.AcknowledgedBy
        );
    }
}
