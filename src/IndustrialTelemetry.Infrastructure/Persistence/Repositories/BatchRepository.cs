using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence.Repositories;

public class BatchRepository : IBatchRepository
{
    private readonly IndustrialDbContext _context;

    public BatchRepository(IndustrialDbContext context)
    {
        _context = context;
    }

    public async Task<Batch?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Batches
            .FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<Batch?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _context.Batches
            .FirstOrDefaultAsync(b => b.Code == normalized, ct);
    }

    public async Task<IReadOnlyList<Batch>> ListAsync(BatchStatus? status = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _context.Batches.AsNoTracking();

        if (status.HasValue)
        {
            query = query.Where(b => b.Status == status.Value);
        }

        return await query
            .OrderByDescending(b => b.CreatedAtUtc)
            .Take(Math.Min(limit, 200))
            .ToListAsync(ct);
    }

    public async Task AddAsync(Batch batch, CancellationToken ct = default)
    {
        await _context.Batches.AddAsync(batch, ct);
    }

    public Task UpdateAsync(Batch batch, CancellationToken ct = default)
    {
        _context.Batches.Update(batch);
        return Task.CompletedTask;
    }
}
