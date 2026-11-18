using System.Diagnostics;
using System.Diagnostics.Metrics;
using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Infrastructure.Outbox;

namespace IndustrialTelemetry.Api.Observability;

/// <summary>
/// OpenTelemetry ActivitySource and Prometheus Meter definitions for IndustrialTelemetry.Engine.
/// </summary>
public sealed class HistorianTelemetry : IDisposable
{
    public const string ServiceName = "IndustrialTelemetry.Engine";
    public static readonly ActivitySource ActivitySource = new(ServiceName, "1.0.0");

    private readonly Meter _meter = new(ServiceName, "1.0.0");

    public HistorianTelemetry(
        ITelemetryChannel channel,
        IObservabilityStream stream,
        IOutboxDispatchMetrics? dispatchMetrics = null)
    {
        _meter.CreateObservableGauge(
            "historian_channel_depth",
            () => channel.GetStatus().CurrentCount,
            unit: "items",
            description: "Current number of telemetry measurements queued in the bounded channel.");

        _meter.CreateObservableGauge(
            "historian_channel_capacity",
            () => channel.GetStatus().Capacity,
            unit: "items",
            description: "Configured maximum capacity of the in-memory telemetry ingestion channel.");

        _meter.CreateObservableCounter(
            "historian_channel_ingested_total",
            () => channel.GetStatus().TotalIngested,
            unit: "items",
            description: "Cumulative telemetry measurements accepted into the ingestion channel.");

        _meter.CreateObservableCounter(
            "historian_channel_drained_total",
            () => channel.GetStatus().TotalDrained,
            unit: "items",
            description: "Cumulative telemetry measurements drained from the channel into PostgreSQL.");

        _meter.CreateObservableCounter(
            "historian_channel_dropped_total",
            () => channel.GetStatus().TotalDropped,
            unit: "items",
            description: "Cumulative telemetry measurements dropped due to channel backpressure.");

        _meter.CreateObservableCounter(
            "historian_outbox_dispatched_total",
            () => dispatchMetrics?.TotalDispatchedCount ?? 0L,
            unit: "messages",
            description: "Cumulative transactional outbox events dispatched.");

        _meter.CreateObservableGauge(
            "historian_sse_subscribers_active",
            () => stream.GetStats().ActiveSubscribers,
            unit: "clients",
            description: "Currently connected Server-Sent Events observability subscribers.");
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
