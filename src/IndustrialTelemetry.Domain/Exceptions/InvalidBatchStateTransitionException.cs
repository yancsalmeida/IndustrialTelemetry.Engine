using IndustrialTelemetry.Domain.Enums;

namespace IndustrialTelemetry.Domain.Exceptions;

public class InvalidBatchStateTransitionException : DomainValidationException
{
    public BatchStatus FromStatus { get; }
    public BatchStatus ToStatus { get; }

    public InvalidBatchStateTransitionException(BatchStatus from, BatchStatus to, string message)
        : base($"Invalid batch state transition from '{from}' to '{to}': {message}")
    {
        FromStatus = from;
        ToStatus = to;
    }
}
