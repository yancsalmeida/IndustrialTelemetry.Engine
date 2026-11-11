using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

public interface ISlidingWindowAnomalyDetector
{
    /// <summary>
    /// Records a measurement into the sliding window for the specified batch and sensor tag,
    /// and returns the current calculated Rate of Change (in units per minute) over the window.
    /// Returns null if insufficient points or time elapsed in the window.
    /// </summary>
    double? RecordAndCalculateRateOfChange(TelemetryMeasurement measurement, int windowSeconds = 60, double minSpanSeconds = 1.0);

    /// <summary>
    /// Evaluates all active alert rules (both static thresholds and sliding-window rate-of-change)
    /// against the measurement. Returns all triggered alerts.
    /// </summary>
    IReadOnlyList<ProcessAlert> Evaluate(TelemetryMeasurement measurement, IEnumerable<AlertRule> rules);

    /// <summary>
    /// Clears sliding window buffers for a completed or aborted batch to prevent memory leaks.
    /// </summary>
    void ClearBatch(Guid batchId);

    /// <summary>
    /// Resets all in-memory sliding window buffers and alert suppressions.
    /// </summary>
    void Reset();
}
