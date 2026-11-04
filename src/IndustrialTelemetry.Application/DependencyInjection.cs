using FluentValidation;
using IndustrialTelemetry.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialTelemetry.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddScoped<BatchService>();
        services.AddScoped<TelemetryIngestionService>();
        services.AddScoped<AlertService>();

        return services;
    }
}
