using System.Collections.Concurrent;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Services;

/// <summary>
/// Thread-safe in-memory sliding window anomaly detector.
/// Tracks high-frequency time-series measurements per batch and sensor,
/// computing instantaneous rate-of-change gradients (e.g. dT/dt in °C/min)
/// to detect early chemical process thermal runaway and rapid depressurization anomalies.
/// </summary>
public class SlidingWindowAnomalyDetector : ISlidingWindowAnomalyDetector
{
    private readonly ConcurrentDictionary<string, WindowBuffer> _buffers = new();
    private readonly TimeSpan _alertCooldown;

    public SlidingWindowAnomalyDetector(TimeSpan? alertCooldown = null)
    {
        // Default alert cooldown: 10 seconds (prevents high-frequency alarm flooding / chattering)
        _alertCooldown = alertCooldown ?? TimeSpan.FromSeconds(10);
    }

    public double? RecordAndCalculateRateOfChange(TelemetryMeasurement measurement, int windowSeconds = 60, double minSpanSeconds = 1.0)
    {
        var buffer = GetOrCreateBuffer(measurement.BatchId, measurement.SensorTag);
        buffer.AddAndPrune(measurement.TimestampUtc, measurement.Value, TimeSpan.FromSeconds(windowSeconds + 15));
        return buffer.CalculateRateOfChange(measurement.TimestampUtc, measurement.Value, windowSeconds, minSpanSeconds);
    }

    public IReadOnlyList<ProcessAlert> Evaluate(TelemetryMeasurement measurement, IEnumerable<AlertRule> rules)
    {
        var alerts = new List<ProcessAlert>();
        var matchingRules = rules
            .Where(r => r.IsActive && string.Equals(r.SensorTag, measurement.SensorTag, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingRules.Count == 0)
            return alerts;

        // 1. Static Level Threshold Rules (High / Low limits)
        foreach (var rule in matchingRules)
        {
            if (rule.MinThreshold.HasValue || rule.MaxThreshold.HasValue)
            {
                var staticAlert = rule.Evaluate(measurement);
                if (staticAlert != null)
                {
                    alerts.Add(staticAlert);
                }
            }
        }

        // 2. Sliding-Window Temporal Rate-of-Change Rules (e.g. dT/dt > 5 °C/min)
        var rocRules = matchingRules
            .Where(r => r.MaxRateOfChangePerMinute.HasValue || r.MinRateOfChangePerMinute.HasValue)
            .ToList();

        if (rocRules.Count > 0)
        {
            int maxWindow = rocRules.Max(r => r.WindowSeconds);
            var buffer = GetOrCreateBuffer(measurement.BatchId, measurement.SensorTag);
            buffer.AddAndPrune(measurement.TimestampUtc, measurement.Value, TimeSpan.FromSeconds(maxWindow + 15));

            foreach (var rule in rocRules)
            {
                var rate = buffer.CalculateRateOfChange(
                    measurement.TimestampUtc,
                    measurement.Value,
                    rule.WindowSeconds,
                    minSpanSeconds: 1.0);

                if (rate.HasValue)
                {
                    var rocAlert = rule.EvaluateRateOfChange(measurement, rate.Value);
                    if (rocAlert != null)
                    {
                        if (buffer.ShouldAlert(rule.Id, measurement.TimestampUtc, _alertCooldown))
                        {
                            alerts.Add(rocAlert);
                        }
                    }
                }
            }
        }

        return alerts;
    }

    public void ClearBatch(Guid batchId)
    {
        var prefix = $"{batchId}:";
        foreach (var key in _buffers.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            _buffers.TryRemove(key, out _);
        }
    }

    public void Reset()
    {
        _buffers.Clear();
    }

    private WindowBuffer GetOrCreateBuffer(Guid batchId, string sensorTag)
    {
        var key = $"{batchId}:{sensorTag.Trim().ToUpperInvariant()}";
        return _buffers.GetOrAdd(key, _ => new WindowBuffer());
    }

    private sealed class WindowBuffer
    {
        private readonly LinkedList<Sample> _samples = new();
        private readonly Dictionary<Guid, DateTime> _lastAlerts = new();
        private readonly object _lock = new();

        public void AddAndPrune(DateTime timestampUtc, double value, TimeSpan maxRetention)
        {
            lock (_lock)
            {
                var cutoff = timestampUtc - maxRetention;
                while (_samples.Count > 0 && _samples.First!.Value.Timestamp < cutoff)
                {
                    _samples.RemoveFirst();
                }

                if (_samples.Count == 0 || timestampUtc >= _samples.Last!.Value.Timestamp)
                {
                    _samples.AddLast(new Sample(timestampUtc, value));
                }
                else
                {
                    // Insertion for rare out-of-order samples
                    var node = _samples.Last;
                    while (node != null && node.Value.Timestamp > timestampUtc)
                    {
                        node = node.Previous;
                    }

                    if (node == null)
                        _samples.AddFirst(new Sample(timestampUtc, value));
                    else
                        _samples.AddAfter(node, new Sample(timestampUtc, value));
                }
            }
        }

        public double? CalculateRateOfChange(DateTime currentTimestampUtc, double currentValue, int windowSeconds, double minSpanSeconds)
        {
            lock (_lock)
            {
                if (_samples.Count < 2)
                    return null;

                var windowStart = currentTimestampUtc.AddSeconds(-windowSeconds);

                Sample? earliest = null;
                foreach (var sample in _samples)
                {
                    if (sample.Timestamp >= windowStart)
                    {
                        earliest = sample;
                        break;
                    }
                }

                if (earliest == null)
                    return null;

                var dtSeconds = (currentTimestampUtc - earliest.Value.Timestamp).TotalSeconds;
                if (dtSeconds < minSpanSeconds)
                    return null;

                var dy = currentValue - earliest.Value.Value;
                var ratePerMinute = (dy / dtSeconds) * 60.0;
                return ratePerMinute;
            }
        }

        public bool ShouldAlert(Guid ruleId, DateTime currentTimestampUtc, TimeSpan cooldown)
        {
            lock (_lock)
            {
                if (cooldown > TimeSpan.Zero &&
                    _lastAlerts.TryGetValue(ruleId, out var lastTime) &&
                    (currentTimestampUtc - lastTime) < cooldown)
                {
                    return false; // Suppress duplicate alert during cooldown
                }

                _lastAlerts[ruleId] = currentTimestampUtc;
                return true;
            }
        }
    }

    private readonly record struct Sample(DateTime Timestamp, double Value);
}
