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

    public async Task<IReadOnlyDictionary<Guid, int>> GetCountsByBatchesAsync(
        IEnumerable<Guid> batchIds,
        CancellationToken ct = default)
    {
        var ids = batchIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, int>();
        }

        return await _context.ProcessAlerts
            .AsNoTracking()
            .Where(a => ids.Contains(a.BatchId))
            .GroupBy(a => a.BatchId)
            .Select(g => new { BatchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BatchId, x => x.Count, ct);
    }

    public async Task<(int TotalAlerts, int UnacknowledgedAlerts, int CriticalAlerts)> GetGlobalCountsAsync(CancellationToken ct = default)
    {
        int total = await _context.ProcessAlerts.AsNoTracking().CountAsync(ct);
        int unacked = await _context.ProcessAlerts.AsNoTracking().CountAsync(a => !a.Acknowledged, ct);
        int critical = await _context.ProcessAlerts.AsNoTracking()
            .CountAsync(a => a.Severity == Domain.Enums.AlertSeverity.Critical, ct);
        return (total, unacked, critical);
    }

    public async Task<IReadOnlyList<ProcessAlert>> GetRecentAlertsAsync(
        Guid? batchId = null,
        string? severity = null,
        bool? unacknowledgedOnly = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        IQueryable<ProcessAlert> query = _context.ProcessAlerts.AsNoTracking();

        if (batchId.HasValue)
            query = query.Where(a => a.BatchId == batchId.Value);

        if (!string.IsNullOrWhiteSpace(severity) &&
            Enum.TryParse<Domain.Enums.AlertSeverity>(severity.Trim(), ignoreCase: true, out var parsedSeverity))
        {
            query = query.Where(a => a.Severity == parsedSeverity);
        }

        if (unacknowledgedOnly == true)
            query = query.Where(a => !a.Acknowledged);

        return await query
            .OrderByDescending(a => a.TimestampUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }
}
