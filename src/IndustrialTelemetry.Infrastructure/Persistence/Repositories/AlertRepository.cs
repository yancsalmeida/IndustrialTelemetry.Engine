using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly IndustrialDbContext _context;

    public AlertRepository(IndustrialDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AlertRule>> GetActiveRulesAsync(CancellationToken ct = default)
    {
        return await _context.AlertRules
            .AsNoTracking()
            .Where(r => r.IsActive)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<AlertRule>> ListAllRulesAsync(CancellationToken ct = default)
    {
        return await _context.AlertRules
            .AsNoTracking()
            .OrderBy(r => r.SensorTag)
            .ToListAsync(ct);
    }

    public async Task AddRuleAsync(AlertRule rule, CancellationToken ct = default)
    {
        await _context.AlertRules.AddAsync(rule, ct);
    }

    public async Task AddAlertsAsync(IEnumerable<ProcessAlert> alerts, CancellationToken ct = default)
    {
        await _context.ProcessAlerts.AddRangeAsync(alerts, ct);
    }

    public async Task<ProcessAlert?> GetAlertByIdAsync(Guid alertId, CancellationToken ct = default)
    {
        return await _context.ProcessAlerts
            .FirstOrDefaultAsync(a => a.Id == alertId, ct);
    }

    public async Task<IReadOnlyList<ProcessAlert>> GetAlertsByBatchIdAsync(Guid batchId, CancellationToken ct = default)
    {
        return await _context.ProcessAlerts
            .AsNoTracking()
            .Where(a => a.BatchId == batchId)
            .OrderByDescending(a => a.TimestampUtc)
            .ToListAsync(ct);
    }

    public async Task<int> GetCountByBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        return await _context.ProcessAlerts
            .AsNoTracking()
            .CountAsync(a => a.BatchId == batchId, ct);
    }
}
