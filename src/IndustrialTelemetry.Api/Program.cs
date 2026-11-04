using System.Text.Json.Serialization;
using IndustrialTelemetry.Api.Middleware;
using IndustrialTelemetry.Application;
using IndustrialTelemetry.Infrastructure;
using IndustrialTelemetry.Infrastructure.Persistence;
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

// 4. Add HealthChecks
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

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "IndustrialTelemetry.Engine v1");
    c.RoutePrefix = string.Empty; // Serve Swagger UI directly at root '/'
});

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
