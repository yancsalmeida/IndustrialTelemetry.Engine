using Dapper;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence.Repositories;

public class TelemetryRepository : ITelemetryRepository
{
    private readonly IndustrialDbContext _context;

    private sealed record SensorAggregateRow(
        string SensorTag,
        double Average,
        double Min,
        double Max,
        long Count);

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

    /// <summary>
    /// Computes per-sensor statistical aggregates (AVG, MIN, MAX, COUNT) for a batch.
    /// Executes server-side SQL GROUP BY via Dapper on relational providers (PostgreSQL / SQLite)
    /// to avoid materializing raw time-series rows in application memory.
    /// </summary>
    public async Task<IReadOnlyList<SensorAggregate>> GetSensorAggregatesByBatchAsync(
        Guid batchId,
        CancellationToken ct = default)
    {
        if (_context.Database.IsRelational())
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            const string sql = """
                SELECT
                    "SensorTag" AS "SensorTag",
                    AVG("Value") AS "Average",
                    MIN("Value") AS "Min",
                    MAX("Value") AS "Max",
                    COUNT(*) AS "Count"
                FROM telemetry_measurements
                WHERE "BatchId" = @BatchId
                GROUP BY "SensorTag"
                ORDER BY "SensorTag";
                """;

            var command = new CommandDefinition(
                sql,
                new { BatchId = batchId },
                cancellationToken: ct);

            var rows = await connection.QueryAsync<SensorAggregateRow>(command);

            return rows
                .Select(r => new SensorAggregate(
                    SensorTag: r.SensorTag,
                    Average: Math.Round(r.Average, 2),
                    Min: Math.Round(r.Min, 2),
                    Max: Math.Round(r.Max, 2),
                    Count: (int)r.Count))
                .ToList();
        }

        var records = await _context.Telemetry
            .AsNoTracking()
            .Where(t => t.BatchId == batchId)
            .ToListAsync(ct);

        return records
            .GroupBy(t => t.SensorTag)
            .OrderBy(g => g.Key)
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
        if (_context.Database.IsRelational())
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            const string sql = """
                SELECT COUNT(*)
                FROM telemetry_measurements
                WHERE "BatchId" = @BatchId;
                """;

            var command = new CommandDefinition(
                sql,
                new { BatchId = batchId },
                cancellationToken: ct);

            var count = await connection.ExecuteScalarAsync<long>(command);
            return (int)count;
        }

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

    public async Task<long> GetTotalCountAsync(CancellationToken ct = default)
    {
        if (_context.Database.IsRelational())
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(ct);

            return await connection.ExecuteScalarAsync<long>(
                new CommandDefinition("SELECT COUNT(*) FROM telemetry_measurements;", cancellationToken: ct));
        }

        return await _context.Telemetry.AsNoTracking().LongCountAsync(ct);
    }

    public async Task<IReadOnlyList<SensorSummaryRecord>> GetSensorSummariesAsync(
        Guid? batchId = null,
        int rateWindowSeconds = 60,
        CancellationToken ct = default)
    {
        int clampedWindowSec = Math.Clamp(rateWindowSeconds, 5, 3600);
        IQueryable<TelemetryMeasurement> recentQuery = _context.Telemetry.AsNoTracking();
        if (batchId.HasValue)
            recentQuery = recentQuery.Where(t => t.BatchId == batchId.Value);

        var recentSamples = await recentQuery
            .OrderByDescending(t => t.TimestampUtc)
            .ThenByDescending(t => t.Id)
            .Take(2000)
            .ToListAsync(ct);

        if (recentSamples.Count == 0)
            return [];

        IReadOnlyDictionary<string, SensorAggregate>? globalAggs = null;
        if (_context.Database.IsRelational())
        {
            var connection = _context.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync(ct);

            string whereClause = batchId.HasValue ? "WHERE \"BatchId\" = @BatchId" : string.Empty;
            string sql = $"""
                SELECT
                    "SensorTag" AS "SensorTag",
                    AVG("Value") AS "Average",
                    MIN("Value") AS "Min",
                    MAX("Value") AS "Max",
                    COUNT(*) AS "Count"
                FROM telemetry_measurements
                {whereClause}
                GROUP BY "SensorTag";
                """;

            var rows = await connection.QueryAsync<SensorAggregateRow>(
                new CommandDefinition(sql, batchId.HasValue ? new { BatchId = batchId.Value } : null, cancellationToken: ct));
            globalAggs = rows.ToDictionary(
                r => r.SensorTag,
                r => new SensorAggregate(r.SensorTag, r.Average, r.Min, r.Max, (int)r.Count));
        }

        return recentSamples
            .GroupBy(t => t.SensorTag)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var ordered = g.OrderBy(x => x.TimestampUtc).ThenBy(x => x.Id).ToList();
                var latest = ordered[^1];
                var windowStart = latest.TimestampUtc.AddSeconds(-clampedWindowSec);
                var inWindow = ordered.Where(x => x.TimestampUtc >= windowStart).ToList();
                if (inWindow.Count < 2 && ordered.Count >= 2)
                    inWindow = ordered.TakeLast(2).ToList();

                double ratePerMin = 0.0;
                if (inWindow.Count >= 2)
                {
                    double dtMin = (inWindow[^1].TimestampUtc - inWindow[0].TimestampUtc).TotalMinutes;
                    if (dtMin > 1e-6)
                        ratePerMin = (inWindow[^1].Value - inWindow[0].Value) / dtMin;
                }

                SensorAggregate? agg = null;
                globalAggs?.TryGetValue(g.Key, out agg);

                return new SensorSummaryRecord(
                    SensorTag: g.Key,
                    Unit: latest.Unit.ToString(),
                    Quality: latest.Quality.ToString(),
                    LatestValue: Math.Round(latest.Value, 4),
                    LatestTimestampUtc: latest.TimestampUtc,
                    MinRecent: Math.Round(agg?.Min ?? ordered.Min(x => x.Value), 4),
                    MaxRecent: Math.Round(agg?.Max ?? ordered.Max(x => x.Value), 4),
                    AvgRecent: Math.Round(agg?.Average ?? ordered.Average(x => x.Value), 4),
                    SampleCount: agg?.Count ?? ordered.Count,
                    RateOfChangePerMin: Math.Round(ratePerMin, 4));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<TelemetryMeasurement>> GetTimeSeriesAsync(
        string sensorTag,
        Guid? batchId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 300,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorTag);
        string tagUpper = sensorTag.Trim().ToUpperInvariant();
        int clampedLimit = Math.Clamp(limit, 1, 2000);

        IQueryable<TelemetryMeasurement> query = _context.Telemetry
            .AsNoTracking()
            .Where(t => t.SensorTag == tagUpper);

        if (batchId.HasValue)
            query = query.Where(t => t.BatchId == batchId.Value);
        if (fromUtc.HasValue)
            query = query.Where(t => t.TimestampUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            query = query.Where(t => t.TimestampUtc <= toUtc.Value);

        var latestDesc = await query
            .OrderByDescending(t => t.TimestampUtc)
            .ThenByDescending(t => t.Id)
            .Take(clampedLimit)
            .ToListAsync(ct);

        latestDesc.Reverse();
        return latestDesc;
    }
}
