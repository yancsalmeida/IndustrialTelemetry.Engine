using System.Text.Json;
using IndustrialTelemetry.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace IndustrialTelemetry.Api.Filters;

public class IdempotencyFilter : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    private readonly IIdempotencyStore _store;
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public IdempotencyFilter(IIdempotencyStore store)
    {
        _store = store;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // Only apply idempotency to state-mutating HTTP methods
        var method = context.HttpContext.Request.Method;
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPut(method) && !HttpMethods.IsPatch(method))
        {
            await next();
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(HeaderName, out var keyValues) ||
            string.IsNullOrWhiteSpace(keyValues.FirstOrDefault()))
        {
            await next();
            return;
        }

        var idempotencyKey = keyValues.First()!.Trim();
        var ct = context.HttpContext.RequestAborted;

        var cached = await _store.TryGetAsync(idempotencyKey, ct);
        if (cached != null)
        {
            context.HttpContext.Response.Headers["X-Cache-Lookup"] = "HIT";
            context.HttpContext.Response.Headers[HeaderName] = idempotencyKey;

            context.Result = new ContentResult
            {
                StatusCode = cached.StatusCode,
                ContentType = cached.ContentType ?? "application/json",
                Content = cached.Body
            };
            return;
        }

        context.HttpContext.Response.Headers["X-Cache-Lookup"] = "MISS";
        context.HttpContext.Response.Headers[HeaderName] = idempotencyKey;

        var executedContext = await next();

        if (executedContext.Result is ObjectResult objectResult &&
            objectResult.StatusCode >= 200 && objectResult.StatusCode < 300)
        {
            var body = objectResult.Value is string str
                ? str
                : JsonSerializer.Serialize(objectResult.Value, JsonOptions);

            var entry = new IdempotencyEntry(
                objectResult.StatusCode ?? StatusCodes.Status200OK,
                "application/json",
                body
            );

            await _store.SetAsync(idempotencyKey, entry, DefaultTtl, ct);
        }
    }
}
