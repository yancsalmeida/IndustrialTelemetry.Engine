using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using IndustrialTelemetry.Api.Filters;
using IndustrialTelemetry.Api.Middleware;
using IndustrialTelemetry.Application;
using IndustrialTelemetry.Infrastructure;
using IndustrialTelemetry.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// 1. Configure Serilog for structured JSON logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Add Clean Architecture Layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 3. Add Controllers & JSON Serializer options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// 4. Rate Limiting & Idempotency Filter
builder.Services.AddScoped<IdempotencyFilter>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("telemetry-rate-limit", opt =>
    {
        opt.PermitLimit = 1000;
        opt.Window = TimeSpan.FromSeconds(10);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 100;
    });
});

// 5. Add HealthChecks
builder.Services.AddHealthChecks();

// 5. Add OpenAPI / Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "IndustrialTelemetry.Engine API",
        Version = "v1",
        Description = "Enterprise-grade Industrial Telemetry, Batch Execution & Safety Alarm Management Web API built with .NET 8, PostgreSQL & Clean Architecture.",
        Contact = new OpenApiContact
        {
            Name = "Yan Almeida",
            Url = new Uri("https://github.com/yancsalmeida")
        }
    });
});

var app = builder.Build();

// 6. Database schema creation & seeding on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<IndustrialDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    await DbInitializer.InitializeAsync(context, logger);
}

// 7. HTTP Request Pipeline
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseSerilogRequestLogging();

// OpenAPI version normalization: Swagger UI regex expects 3.0.[0-3], normalize 3.0.4 to 3.0.1
app.Use(async (context, next) =>
{
    if (context.Request.Path.Value != null && context.Request.Path.Value.EndsWith("/swagger.json"))
    {
        var originalBodyStream = context.Response.Body;
        using var memoryStream = new MemoryStream();
        context.Response.Body = memoryStream;

        await next();

        memoryStream.Position = 0;
        using var reader = new StreamReader(memoryStream);
        var responseBody = await reader.ReadToEndAsync();

        responseBody = System.Text.RegularExpressions.Regex.Replace(
            responseBody,
            @"""openapi""\s*:\s*""3\.0\.\d+""",
            @"""openapi"": ""3.0.1"""
        );

        context.Response.Body = originalBodyStream;
        context.Response.Headers.ContentLength = System.Text.Encoding.UTF8.GetByteCount(responseBody);
        await context.Response.WriteAsync(responseBody);
        return;
    }

    await next();
});

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IndustrialTelemetry.Engine v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI directly at root '/'
});

app.UseRateLimiter();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
