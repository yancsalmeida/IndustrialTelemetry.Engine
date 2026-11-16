using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

/// <summary>
/// Persistence contract for querying and updating transactional outbox messages.
/// </summary>
public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, int maxRetries, CancellationToken ct = default);
    Task UpdateAsync(OutboxMessage message, CancellationToken ct = default);
    Task UpdateRangeAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct = default);
    Task<int> CleanupProcessedAsync(DateTime olderThanUtc, CancellationToken ct = default);
    Task<int> GetPendingCountAsync(CancellationToken ct = default);
    Task<(int PendingCount, int ProcessedCount, int DeadLetterCount)> GetSummaryCountsAsync(int maxRetries = 5, CancellationToken ct = default);
    Task<IReadOnlyList<OutboxMessage>> GetRecentMessagesAsync(
        bool? pendingOnly = null,
        bool? deadLetterOnly = null,
        int maxRetries = 5,
        int limit = 100,
        CancellationToken ct = default);
    Task<int> RequeueFailedMessagesAsync(int maxRetries = 5, CancellationToken ct = default);
}
