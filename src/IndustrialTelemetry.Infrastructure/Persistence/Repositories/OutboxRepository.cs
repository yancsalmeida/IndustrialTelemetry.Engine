using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence.Repositories;

public class OutboxRepository : IOutboxRepository
{
    private readonly IndustrialDbContext _context;

    public OutboxRepository(IndustrialDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken ct = default)
    {
        await _context.OutboxMessages.AddAsync(message, ct);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize,
        int maxRetries,
        CancellationToken ct = default)
    {
        return await _context.OutboxMessages
            .Where(x => x.ProcessedOnUtc == null && x.RetryCount < maxRetries)
            .OrderBy(x => x.OccurredOnUtc)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    public Task UpdateAsync(OutboxMessage message, CancellationToken ct = default)
    {
        _context.OutboxMessages.Update(message);
        return Task.CompletedTask;
    }

    public Task UpdateRangeAsync(IEnumerable<OutboxMessage> messages, CancellationToken ct = default)
    {
        _context.OutboxMessages.UpdateRange(messages);
        return Task.CompletedTask;
    }

    public async Task<int> CleanupProcessedAsync(DateTime olderThanUtc, CancellationToken ct = default)
    {
        return await _context.OutboxMessages
            .Where(x => x.ProcessedOnUtc != null && x.ProcessedOnUtc < olderThanUtc)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<int> GetPendingCountAsync(CancellationToken ct = default)
    {
        return await _context.OutboxMessages
            .CountAsync(x => x.ProcessedOnUtc == null, ct);
    }
}
