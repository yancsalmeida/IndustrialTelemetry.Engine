using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence.Repositories;

public class TelemetryRepository : ITelemetryRepository
{
    private readonly IndustrialDbContext _context;

    public TelemetryRepository(IndustrialDbContext context)
    {
        _context = context;
    }

    public async Task IngestBatchAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default)
    {
        await _context.Telemetry.AddRangeAsync(measurements, ct);
    }

    public async Task<IReadOnlyList<TelemetryMeasurement>> GetByBatchIdAsync(
        Guid batchId,
        string? sensorTag = null,
        int limit = 500,
        CancellationToken ct = default)
    {
        var query = _context.Telemetry
            .AsNoTracking()
            .Where(t => t.BatchId == batchId);

        if (!string.IsNullOrWhiteSpace(sensorTag))
        {
            var tagUpper = sensorTag.Trim().ToUpperInvariant();
            query = query.Where(t => t.SensorTag == tagUpper);
        }

        return await query
            .OrderBy(t => t.TimestampUtc)
            .Take(Math.Min(limit, 2000))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SensorAggregate>> GetSensorAggregatesByBatchAsync(
        Guid batchId,
        CancellationToken ct = default)
    {
        var records = await _context.Telemetry
            .AsNoTracking()
            .Where(t => t.BatchId == batchId)
            .ToListAsync(ct);

        return records
            .GroupBy(t => t.SensorTag)
            .Select(g => new SensorAggregate(
                SensorTag: g.Key,
                Average: Math.Round(g.Average(x => x.Value), 2),
                Min: Math.Round(g.Min(x => x.Value), 2),
                Max: Math.Round(g.Max(x => x.Value), 2),
                Count: g.Count()
            ))
            .ToList();
    }

    public async Task<int> GetCountByBatchAsync(Guid batchId, CancellationToken ct = default)
    {
        return await _context.Telemetry
            .AsNoTracking()
            .CountAsync(t => t.BatchId == batchId, ct);
    }
}
