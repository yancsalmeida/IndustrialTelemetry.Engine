using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Application.Interfaces;

public interface IBatchRepository
{
    Task<Batch?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Batch?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Batch>> ListAsync(BatchStatus? status = null, int limit = 50, CancellationToken ct = default);
    Task AddAsync(Batch batch, CancellationToken ct = default);
    Task UpdateAsync(Batch batch, CancellationToken ct = default);
}
