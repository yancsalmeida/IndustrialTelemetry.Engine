using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

public interface IAlertRepository
{
    Task<IReadOnlyList<AlertRule>> GetActiveRulesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AlertRule>> ListAllRulesAsync(CancellationToken ct = default);
    Task AddRuleAsync(AlertRule rule, CancellationToken ct = default);
    Task AddAlertsAsync(IEnumerable<ProcessAlert> alerts, CancellationToken ct = default);
    Task<ProcessAlert?> GetAlertByIdAsync(Guid alertId, CancellationToken ct = default);
    Task<IReadOnlyList<ProcessAlert>> GetAlertsByBatchIdAsync(Guid batchId, CancellationToken ct = default);
    Task<int> GetCountByBatchAsync(Guid batchId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, int>> GetCountsByBatchesAsync(IEnumerable<Guid> batchIds, CancellationToken ct = default);
}
