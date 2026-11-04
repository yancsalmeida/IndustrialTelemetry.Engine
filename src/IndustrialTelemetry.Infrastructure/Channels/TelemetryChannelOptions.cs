namespace IndustrialTelemetry.Infrastructure.Channels;

public class TelemetryChannelOptions
{
    public const string SectionName = "TelemetryChannel";

    /// <summary>
    /// Bounded in-memory channel capacity limit.
    /// </summary>
    public int Capacity { get; set; } = 50000;

    /// <summary>
    /// Maximum number of measurements drained and written in a single database bulk copy batch.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum latency window in milliseconds before flushing an incomplete batch to the database.
    /// </summary>
    public int FlushIntervalMs { get; set; } = 50;
}
