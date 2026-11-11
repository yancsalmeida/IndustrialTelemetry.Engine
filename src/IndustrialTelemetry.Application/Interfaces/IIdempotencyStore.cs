namespace IndustrialTelemetry.Application.Interfaces;

public record IdempotencyEntry(
    int StatusCode,
    string? ContentType,
    string Body
);

public interface IIdempotencyStore
{
    ValueTask<IdempotencyEntry?> TryGetAsync(string key, CancellationToken ct = default);
    ValueTask SetAsync(string key, IdempotencyEntry entry, TimeSpan ttl, CancellationToken ct = default);
}
