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

    public async Task<(int PendingCount, int ProcessedCount, int DeadLetterCount)> GetSummaryCountsAsync(
        int maxRetries = 5,
        CancellationToken ct = default)
    {
        int pending = await _context.OutboxMessages
            .AsNoTracking()
            .CountAsync(x => x.ProcessedOnUtc == null && x.RetryCount < maxRetries, ct);
        int processed = await _context.OutboxMessages
            .AsNoTracking()
            .CountAsync(x => x.ProcessedOnUtc != null, ct);
        int deadLetter = await _context.OutboxMessages
            .AsNoTracking()
            .CountAsync(x => x.ProcessedOnUtc == null && x.RetryCount >= maxRetries, ct);
        return (pending, processed, deadLetter);
    }

    public async Task<IReadOnlyList<OutboxMessage>> GetRecentMessagesAsync(
        bool? pendingOnly = null,
        bool? deadLetterOnly = null,
        int maxRetries = 5,
        int limit = 100,
        CancellationToken ct = default)
    {
        IQueryable<OutboxMessage> query = _context.OutboxMessages.AsNoTracking();

        if (deadLetterOnly == true)
            query = query.Where(x => x.ProcessedOnUtc == null && x.RetryCount >= maxRetries);
        else if (pendingOnly == true)
            query = query.Where(x => x.ProcessedOnUtc == null && x.RetryCount < maxRetries);

        return await query
            .OrderByDescending(x => x.OccurredOnUtc)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
    }

    public async Task<int> RequeueFailedMessagesAsync(int maxRetries = 5, CancellationToken ct = default)
    {
        var failed = await _context.OutboxMessages
            .Where(x => x.ProcessedOnUtc == null && x.RetryCount > 0)
            .ToListAsync(ct);

        foreach (var msg in failed)
            msg.RequeueForRetry();

        return failed.Count;
    }
}
