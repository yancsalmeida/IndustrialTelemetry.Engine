namespace IndustrialTelemetry.Infrastructure.Outbox;

/// <summary>
/// Configuration options for the Transactional Outbox Background Processor.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Maximum number of pending outbox messages to fetch and process in a single cycle.
    /// </summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>
    /// Interval in milliseconds between outbox poll cycles.
    /// </summary>
    public int PollIntervalMs { get; set; } = 250;

    /// <summary>
    /// Maximum retry attempts before marking an outbox message as dead-lettered / failed.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Automatically delete successfully processed outbox messages older than retention window.
    /// </summary>
    public bool AutoCleanup { get; set; } = false;

    /// <summary>
    /// Retention period in days for processed outbox messages.
    /// </summary>
    public int RetentionDays { get; set; } = 7;
}
