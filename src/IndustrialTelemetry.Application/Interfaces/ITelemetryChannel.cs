using System.Diagnostics.CodeAnalysis;
using IndustrialTelemetry.Application.DTOs;
using IndustrialTelemetry.Domain.Entities;

namespace IndustrialTelemetry.Application.Interfaces;

/// <summary>
/// High-throughput asynchronous bounded in-memory channel decoupling incoming telemetry ingestion from persistence.
/// </summary>
public interface ITelemetryChannel
{
    int Capacity { get; }
    int CurrentCount { get; }
    ValueTask<bool> WriteAsync(TelemetryMeasurement measurement, CancellationToken ct = default);
    ValueTask<int> WriteBatchAsync(IEnumerable<TelemetryMeasurement> measurements, CancellationToken ct = default);
    bool TryWrite(TelemetryMeasurement measurement);
    bool TryRead([NotNullWhen(true)] out TelemetryMeasurement? measurement);
    ValueTask<bool> WaitToReadAsync(CancellationToken ct = default);
    IAsyncEnumerable<TelemetryMeasurement> ReadAllAsync(CancellationToken ct = default);
    ChannelStatusDto GetStatus();
    void CompleteWriter(Exception? error = null);
}
