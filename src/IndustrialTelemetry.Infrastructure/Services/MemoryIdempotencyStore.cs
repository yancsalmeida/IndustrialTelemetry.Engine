using IndustrialTelemetry.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace IndustrialTelemetry.Infrastructure.Services;

public class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly IMemoryCache _cache;

    public MemoryIdempotencyStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public ValueTask<IdempotencyEntry?> TryGetAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ValueTask.FromResult<IdempotencyEntry?>(null);

        var cacheKey = $"idempotency:{key.Trim()}";
        if (_cache.TryGetValue(cacheKey, out IdempotencyEntry? entry))
        {
            return ValueTask.FromResult(entry);
        }

        return ValueTask.FromResult<IdempotencyEntry?>(null);
    }

    public ValueTask SetAsync(string key, IdempotencyEntry entry, TimeSpan ttl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key) || entry == null)
            return ValueTask.CompletedTask;

        var cacheKey = $"idempotency:{key.Trim()}";
        _cache.Set(cacheKey, entry, ttl);
        return ValueTask.CompletedTask;
    }
}
