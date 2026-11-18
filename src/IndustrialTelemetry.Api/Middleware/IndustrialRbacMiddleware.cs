using Microsoft.AspNetCore.Mvc;

namespace IndustrialTelemetry.Api.Middleware;

public enum IndustrialRole
{
    Anonymous = 0,
    Viewer = 1,
    Operator = 2,
    Engineer = 3
}

/// <summary>
/// Lightweight API-Key / Bearer token RBAC guardrail for industrial mutating operations.
/// Always validates explicit credentials and role hierarchy; enforces mandatory auth on protected
/// mutations when INDUSTRIAL_ENFORCE_RBAC=true (or IndustrialAuth:EnforceRbac=true).
/// </summary>
public sealed class IndustrialRbacMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IndustrialRbacMiddleware> _logger;
    private readonly bool _enforceRbac;
    private readonly string _viewerKey;
    private readonly string _operatorKey;
    private readonly string _engineerKey;

    public IndustrialRbacMiddleware(
        RequestDelegate next,
        ILogger<IndustrialRbacMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _enforceRbac = ParseBoolEnv("INDUSTRIAL_ENFORCE_RBAC")
            ?? configuration.GetValue<bool>("IndustrialAuth:EnforceRbac");
        _viewerKey = Environment.GetEnvironmentVariable("INDUSTRIAL_VIEWER_KEY")
            ?? configuration["IndustrialAuth:ViewerKey"]
            ?? "viewer-ro-key";
        _operatorKey = Environment.GetEnvironmentVariable("INDUSTRIAL_OPERATOR_KEY")
            ?? configuration["IndustrialAuth:OperatorKey"]
            ?? "operator-ctrl-key";
        _engineerKey = Environment.GetEnvironmentVariable("INDUSTRIAL_ENGINEER_KEY")
            ?? configuration["IndustrialAuth:EngineerKey"]
            ?? "engineer-admin-key";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        IndustrialRole requiredRole = GetRequiredRole(context.Request.Method, context.Request.Path.Value ?? string.Empty);
        string? suppliedKey = ExtractCredential(context.Request);

        IndustrialRole actorRole = IndustrialRole.Anonymous;
        if (!string.IsNullOrWhiteSpace(suppliedKey))
        {
            if (string.Equals(suppliedKey, _engineerKey, StringComparison.Ordinal))
                actorRole = IndustrialRole.Engineer;
            else if (string.Equals(suppliedKey, _operatorKey, StringComparison.Ordinal))
                actorRole = IndustrialRole.Operator;
            else if (string.Equals(suppliedKey, _viewerKey, StringComparison.Ordinal))
                actorRole = IndustrialRole.Viewer;
            else
            {
                await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Invalid API Key",
                    "The supplied X-Api-Key or Bearer token is not recognized.");
                return;
            }

            context.Response.OnStarting(() =>
            {
                context.Response.Headers["X-Auth-Role"] = actorRole.ToString();
                return Task.CompletedTask;
            });
        }

        if (requiredRole > IndustrialRole.Anonymous)
        {
            if (actorRole == IndustrialRole.Anonymous && _enforceRbac)
            {
                await WriteProblemAsync(context, StatusCodes.Status401Unauthorized, "Authentication Required",
                    $"Missing X-Api-Key or Bearer token for protected endpoint requiring role '{requiredRole}'.");
                return;
            }

            if (actorRole != IndustrialRole.Anonymous && actorRole < requiredRole)
            {
                _logger.LogWarning(
                    "RBAC Forbidden: role {ActorRole} attempted {Method} {Path} requiring {RequiredRole}",
                    actorRole, context.Request.Method, context.Request.Path, requiredRole);
                await WriteProblemAsync(context, StatusCodes.Status403Forbidden, "Insufficient Role",
                    $"Role '{actorRole}' cannot execute '{context.Request.Method} {context.Request.Path}'. Required: '{requiredRole}'.");
                return;
            }

            if (actorRole >= requiredRole)
            {
                _logger.LogInformation(
                    "RBAC Audit: role={ActorRole} method={Method} path={Path}",
                    actorRole, context.Request.Method, context.Request.Path);
            }
        }

        await _next(context);
    }

    private static IndustrialRole GetRequiredRole(string method, string path)
    {
        if (!HttpMethods.IsPost(method) && !HttpMethods.IsPut(method) &&
            !HttpMethods.IsPatch(method) && !HttpMethods.IsDelete(method))
        {
            return IndustrialRole.Anonymous;
        }

        if (path.StartsWith("/api/observability/outbox/requeue-failed", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/alerts/rules", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/abort", StringComparison.OrdinalIgnoreCase))
        {
            return IndustrialRole.Engineer;
        }

        if (path.StartsWith("/api/telemetry", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/batches", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/alerts", StringComparison.OrdinalIgnoreCase))
        {
            return IndustrialRole.Operator;
        }

        return IndustrialRole.Anonymous;
    }

    private static string? ExtractCredential(HttpRequest req)
    {
        if (req.Headers.TryGetValue("X-Api-Key", out var keyValues) && !string.IsNullOrWhiteSpace(keyValues))
            return keyValues.ToString().Trim();

        if (req.Headers.TryGetValue("Authorization", out var authValues))
        {
            string raw = authValues.ToString().Trim();
            if (raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return raw["Bearer ".Length..].Trim();
        }

        return null;
    }

    private static bool? ParseBoolEnv(string name)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        return bool.TryParse(raw, out bool val) ? val : null;
    }

    private static Task WriteProblemAsync(HttpContext context, int statusCode, string title, string detail)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        });
    }
}
