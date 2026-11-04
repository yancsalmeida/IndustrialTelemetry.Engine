using IndustrialTelemetry.Application.Interfaces;

namespace IndustrialTelemetry.Infrastructure.Persistence;

public class UnitOfWork : IUnitOfWork
{
    private readonly IndustrialDbContext _context;

    public UnitOfWork(IndustrialDbContext context)
    {
        _context = context;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
