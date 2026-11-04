using IndustrialTelemetry.Domain.Enums;
using IndustrialTelemetry.Domain.Exceptions;

namespace IndustrialTelemetry.Domain.Entities;

public class Batch
{
    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string ProductRecipe { get; private set; } = string.Empty;
    public string ReactorId { get; private set; } = string.Empty;
    public double TargetVolumeM3 { get; private set; }
    public BatchStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? StartTimeUtc { get; private set; }
    public DateTime? EndTimeUtc { get; private set; }
    public string? AbortReason { get; private set; }
    public string? OperatorNotes { get; private set; }

    // Navigation property for telemetry
    private readonly List<TelemetryMeasurement> _telemetry = new();
    public IReadOnlyCollection<TelemetryMeasurement> Telemetry => _telemetry.AsReadOnly();

    // Navigation property for alerts
    private readonly List<ProcessAlert> _alerts = new();
    public IReadOnlyCollection<ProcessAlert> Alerts => _alerts.AsReadOnly();

    // Required by EF Core
    private Batch() { }

    public static Batch Create(
        string code,
        string productRecipe,
        string reactorId,
        double targetVolumeM3,
        string? operatorNotes = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainValidationException("Batch code is required.");

        if (string.IsNullOrWhiteSpace(productRecipe))
            throw new DomainValidationException("Product recipe is required.");

        if (string.IsNullOrWhiteSpace(reactorId))
            throw new DomainValidationException("Reactor identifier is required.");

        if (targetVolumeM3 <= 0.0)
            throw new DomainValidationException("Target volume must be strictly positive.");

        return new Batch
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToUpperInvariant(),
            ProductRecipe = productRecipe.Trim(),
            ReactorId = reactorId.Trim().ToUpperInvariant(),
            TargetVolumeM3 = targetVolumeM3,
            Status = BatchStatus.Scheduled,
            CreatedAtUtc = DateTime.UtcNow,
            OperatorNotes = operatorNotes?.Trim()
        };
    }

    public void Start(DateTime timestampUtc)
    {
        if (Status != BatchStatus.Scheduled)
        {
            throw new InvalidBatchStateTransitionException(
                Status,
                BatchStatus.InProgress,
                "Only batches in 'Scheduled' status can be started.");
        }

        Status = BatchStatus.InProgress;
        StartTimeUtc = timestampUtc;
    }

    public void Complete(DateTime timestampUtc)
    {
        if (Status != BatchStatus.InProgress)
        {
            throw new InvalidBatchStateTransitionException(
                Status,
                BatchStatus.Completed,
                "Only batches currently 'InProgress' can be completed.");
        }

        if (StartTimeUtc.HasValue && timestampUtc < StartTimeUtc.Value)
        {
            throw new DomainValidationException("Completion timestamp cannot precede batch start time.");
        }

        Status = BatchStatus.Completed;
        EndTimeUtc = timestampUtc;
    }

    public void Abort(string reason, DateTime timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainValidationException("A clear abort reason is mandatory for regulatory compliance.");
        }

        if (Status == BatchStatus.Completed)
        {
            throw new InvalidBatchStateTransitionException(
                Status,
                BatchStatus.Aborted,
                "A successfully completed batch cannot be retroactively aborted.");
        }

        if (Status == BatchStatus.Aborted)
        {
            throw new InvalidBatchStateTransitionException(
                Status,
                BatchStatus.Aborted,
                "Batch is already in 'Aborted' status.");
        }

        Status = BatchStatus.Aborted;
        EndTimeUtc = timestampUtc;
        AbortReason = reason.Trim();
    }

    public void UpdateNotes(string notes)
    {
        OperatorNotes = notes?.Trim();
    }
}
