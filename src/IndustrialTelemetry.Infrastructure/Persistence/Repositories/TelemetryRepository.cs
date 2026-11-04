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

    /// <summary>
    /// High-throughput bulk insertion utilizing PostgreSQL Binary COPY (COPY ... FROM STDIN BINARY),
    /// streaming raw bytes directly to PostgreSQL without EF Core change-tracking overhead.
    /// Falls back to EF Core AddRangeAsync in non-PostgreSQL test environments.
    /// </summary>
    public async Task<int> BulkCopyAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default)
    {
        var list = measurements as IReadOnlyCollection<TelemetryMeasurement> ?? measurements.ToList();
        if (list.Count == 0)
            return 0;

        if (_context.Database.IsRelational() && _context.Database.GetDbConnection() is Npgsql.NpgsqlConnection npgsqlConn)
        {
            if (npgsqlConn.State != System.Data.ConnectionState.Open)
            {
                await npgsqlConn.OpenAsync(ct);
            }

            const string copySql = "COPY telemetry_measurements (\"BatchId\", \"TimestampUtc\", \"SensorTag\", \"Value\", \"Unit\", \"Quality\") FROM STDIN (FORMAT BINARY)";

            await using var writer = await npgsqlConn.BeginBinaryImportAsync(copySql, ct);

            foreach (var m in list)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(m.BatchId, NpgsqlTypes.NpgsqlDbType.Uuid, ct);
                await writer.WriteAsync(DateTime.SpecifyKind(m.TimestampUtc, DateTimeKind.Utc), NpgsqlTypes.NpgsqlDbType.TimestampTz, ct);
                await writer.WriteAsync(m.SensorTag, NpgsqlTypes.NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(m.Value, NpgsqlTypes.NpgsqlDbType.Double, ct);
                await writer.WriteAsync(m.Unit.ToString(), NpgsqlTypes.NpgsqlDbType.Varchar, ct);
                await writer.WriteAsync(m.Quality.ToString(), NpgsqlTypes.NpgsqlDbType.Varchar, ct);
            }

            var rowsImported = await writer.CompleteAsync(ct);
            return (int)rowsImported;
        }
        else
        {
            // Non-PostgreSQL fallback (InMemory, SQLite, Mock)
            await _context.Telemetry.AddRangeAsync(list, ct);
            await _context.SaveChangesAsync(ct);
            return list.Count;
        }
    }
}
