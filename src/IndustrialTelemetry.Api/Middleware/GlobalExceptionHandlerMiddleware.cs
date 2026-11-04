using System.Net;
using System.Text.Json;
using IndustrialTelemetry.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialTelemetry.Api.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var statusCode = HttpStatusCode.InternalServerError;
        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path,
            Status = (int)statusCode,
            Title = "An unexpected error occurred processing your industrial telemetry request."
        };

        switch (exception)
        {
            case EntityNotFoundException notFoundEx:
                statusCode = HttpStatusCode.NotFound;
                problemDetails.Status = (int)statusCode;
                problemDetails.Title = "Resource Not Found";
                problemDetails.Detail = notFoundEx.Message;
                _logger.LogWarning("Entity not found: {Message}", notFoundEx.Message);
                break;

            case DomainValidationException domainEx:
                statusCode = HttpStatusCode.BadRequest;
                problemDetails.Status = (int)statusCode;
                problemDetails.Title = "Business Rule / Validation Violation";
                problemDetails.Detail = domainEx.Message;
                _logger.LogWarning("Domain validation breach: {Message}", domainEx.Message);
                break;

            default:
                _logger.LogError(exception, "Unhandled exception occurred during request execution: {Path}", context.Request.Path);
                problemDetails.Detail = "Internal server error. Review server logs for correlation ID.";
                break;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;

        var json = JsonSerializer.Serialize(problemDetails, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await context.Response.WriteAsync(json);
    }
}
