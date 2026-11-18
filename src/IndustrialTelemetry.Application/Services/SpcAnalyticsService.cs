using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Services;

/// <summary>
/// Statistical Process Control (Shewhart + EWMA) and Golden Batch trajectory comparison service.
/// </summary>
public sealed class SpcAnalyticsService
{
    private readonly ITelemetryRepository _telemetryRepo;

    public SpcAnalyticsService(ITelemetryRepository telemetryRepo)
    {
        _telemetryRepo = telemetryRepo;
    }

    public async Task<SpcControlChartDto> AnalyzeSpcAsync(
        string sensorTag,
        Guid? batchId = null,
        double lambda = 0.2,
        double sigmaMultiplier = 3.0,
        int limit = 300,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorTag);
        var samples = await _telemetryRepo.GetTimeSeriesAsync(sensorTag, batchId, limit: limit, ct: ct);
        return ComputeSpcChart(sensorTag.Trim().ToUpperInvariant(), batchId, samples, lambda, sigmaMultiplier);
    }

    public async Task<GoldenBatchComparisonDto> CompareGoldenBatchAsync(
        string sensorTag,
        Guid referenceBatchId,
        Guid candidateBatchId,
        int limit = 500,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sensorTag);
        string tag = sensorTag.Trim().ToUpperInvariant();
        var goldenSamples = await _telemetryRepo.GetTimeSeriesAsync(tag, referenceBatchId, limit: limit, ct: ct);
        var candidateSamples = await _telemetryRepo.GetTimeSeriesAsync(tag, candidateBatchId, limit: limit, ct: ct);

        return ComputeGoldenBatchComparison(tag, referenceBatchId, candidateBatchId, goldenSamples, candidateSamples);
    }

    public static SpcControlChartDto ComputeSpcChart(
        string sensorTag,
        Guid? batchId,
        IReadOnlyList<TelemetryMeasurement> samples,
        double lambda = 0.2,
        double sigmaMultiplier = 3.0)
    {
        double clampedLambda = Math.Clamp(lambda, 0.05, 1.0);
        double clampedSigma = Math.Clamp(sigmaMultiplier, 1.0, 6.0);

        if (samples.Count == 0)
        {
            return new SpcControlChartDto(
                sensorTag, batchId, 0, clampedLambda, clampedSigma,
                0.0, 0.0, 0.0, 0.0, 0, 0, 0.0, []);
        }

        double mean = samples.Average(s => s.Value);
        double variance = samples.Count > 1
            ? samples.Sum(s => Math.Pow(s.Value - mean, 2)) / (samples.Count - 1)
            : 0.0;
        double stdDev = Math.Sqrt(Math.Max(0.0, variance));

        double shewhartUcl = mean + clampedSigma * stdDev;
        double shewhartLcl = mean - clampedSigma * stdDev;

        double ewma = mean;
        int shewhartBreaches = 0;
        int ewmaBreaches = 0;
        double maxZScore = 0.0;
        var points = new List<SpcPointDto>(samples.Count);

        for (int i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            int t = i + 1;
            ewma = clampedLambda * s.Value + (1.0 - clampedLambda) * ewma;

            // Exact time-varying Montgomery EWMA limits
            double ewmaFactor = Math.Sqrt((clampedLambda / (2.0 - clampedLambda)) * (1.0 - Math.Pow(1.0 - clampedLambda, 2 * t)));
            double ewmaHalfWidth = clampedSigma * stdDev * ewmaFactor;
            double ewmaUcl = mean + ewmaHalfWidth;
            double ewmaLcl = mean - ewmaHalfWidth;

            bool shewhartOoc = stdDev > 1e-12 && (s.Value > shewhartUcl || s.Value < shewhartLcl);
            bool ewmaOoc = stdDev > 1e-12 && (ewma > ewmaUcl || ewma < ewmaLcl);

            if (shewhartOoc) shewhartBreaches++;
            if (ewmaOoc) ewmaBreaches++;

            if (stdDev > 1e-12)
            {
                double z = Math.Abs(s.Value - mean) / stdDev;
                if (z > maxZScore) maxZScore = z;
            }

            points.Add(new SpcPointDto(
                TimestampUtc: s.TimestampUtc,
                RawValue: Math.Round(s.Value, 4),
                EwmaValue: Math.Round(ewma, 4),
                EwmaUpperLimit: Math.Round(ewmaUcl, 4),
                EwmaLowerLimit: Math.Round(ewmaLcl, 4),
                ShewhartOutOfControl: shewhartOoc,
                EwmaOutOfControl: ewmaOoc));
        }

        return new SpcControlChartDto(
            SensorTag: sensorTag,
            BatchId: batchId,
            SampleCount: samples.Count,
            Lambda: Math.Round(clampedLambda, 4),
            SigmaMultiplier: Math.Round(clampedSigma, 2),
            Mean: Math.Round(mean, 4),
            StdDev: Math.Round(stdDev, 4),
            ShewhartUcl: Math.Round(shewhartUcl, 4),
            ShewhartLcl: Math.Round(shewhartLcl, 4),
            ShewhartBreachCount: shewhartBreaches,
            EwmaBreachCount: ewmaBreaches,
            MaxZScore: Math.Round(maxZScore, 3),
            Points: points);
    }

    public static GoldenBatchComparisonDto ComputeGoldenBatchComparison(
        string sensorTag,
        Guid referenceBatchId,
        Guid candidateBatchId,
        IReadOnlyList<TelemetryMeasurement> goldenSamples,
        IReadOnlyList<TelemetryMeasurement> candidateSamples)
    {
        if (goldenSamples.Count == 0 || candidateSamples.Count == 0)
        {
            return new GoldenBatchComparisonDto(
                sensorTag, referenceBatchId, candidateBatchId,
                0, 0.0, 0.0, 0.0, 0.0, []);
        }

        DateTime t0Golden = goldenSamples[0].TimestampUtc;
        DateTime t0Cand = candidateSamples[0].TimestampUtc;

        var candPoints = candidateSamples
            .Select(c => (Elapsed: Math.Max(0.0, (c.TimestampUtc - t0Cand).TotalSeconds), c.Value))
            .OrderBy(p => p.Elapsed)
            .ToList();

        double candMaxElapsed = candPoints[^1].Elapsed;
        var aligned = new List<GoldenBatchPointDto>(goldenSamples.Count);
        double sumSq = 0.0;
        double sumAbs = 0.0;
        double sumSigned = 0.0;
        double maxAbs = 0.0;

        int candIdx = 0;
        foreach (var g in goldenSamples)
        {
            double t = Math.Max(0.0, (g.TimestampUtc - t0Golden).TotalSeconds);
            if (t > candMaxElapsed && aligned.Count > 0)
                break;

            while (candIdx + 1 < candPoints.Count && candPoints[candIdx + 1].Elapsed <= t)
                candIdx++;

            double candVal;
            if (candIdx + 1 < candPoints.Count)
            {
                var p0 = candPoints[candIdx];
                var p1 = candPoints[candIdx + 1];
                double span = p1.Elapsed - p0.Elapsed;
                candVal = span > 1e-9
                    ? p0.Value + ((t - p0.Elapsed) / span) * (p1.Value - p0.Value)
                    : p0.Value;
            }
            else
            {
                candVal = candPoints[^1].Value;
            }

            double residual = candVal - g.Value;
            double absErr = Math.Abs(residual);
            sumSq += residual * residual;
            sumAbs += absErr;
            sumSigned += residual;
            if (absErr > maxAbs) maxAbs = absErr;

            aligned.Add(new GoldenBatchPointDto(
                ElapsedSeconds: Math.Round(t, 3),
                GoldenValue: Math.Round(g.Value, 4),
                CandidateValue: Math.Round(candVal, 4),
                Residual: Math.Round(residual, 4)));
        }

        int n = aligned.Count;
        return new GoldenBatchComparisonDto(
            SensorTag: sensorTag,
            ReferenceBatchId: referenceBatchId,
            CandidateBatchId: candidateBatchId,
            AlignedPointCount: n,
            Rmse: n > 0 ? Math.Round(Math.Sqrt(sumSq / n), 4) : 0.0,
            Mae: n > 0 ? Math.Round(sumAbs / n, 4) : 0.0,
            MaxAbsoluteDeviation: Math.Round(maxAbs, 4),
            MeanBias: n > 0 ? Math.Round(sumSigned / n, 4) : 0.0,
            AlignedPoints: aligned);
    }
}
