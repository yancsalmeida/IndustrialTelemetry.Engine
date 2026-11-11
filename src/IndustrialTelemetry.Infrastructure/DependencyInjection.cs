using IndustrialTelemetry.Application.Interfaces;
using IndustrialTelemetry.Infrastructure.BackgroundServices;
using IndustrialTelemetry.Infrastructure.Channels;
using IndustrialTelemetry.Infrastructure.Persistence;
using IndustrialTelemetry.Infrastructure.Persistence.Repositories;
using IndustrialTelemetry.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IndustrialTelemetry.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=industrial_telemetry;Username=postgres;Password=postgres";

        services.AddDbContext<IndustrialDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(IndustrialDbContext).Assembly.FullName);
                npgsqlOptions.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
            });
        });

        services.AddScoped<IBatchRepository, BatchRepository>();
        services.AddScoped<ITelemetryRepository, TelemetryRepository>();
        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Transactional Outbox Pattern & Event Publisher
        services.Configure<Outbox.OutboxOptions>(configuration.GetSection(Outbox.OutboxOptions.SectionName));
        services.AddSingleton<IEventPublisher, Outbox.InMemoryEventPublisher>();
        services.AddHostedService<OutboxProcessorBackgroundService>();

        // High-throughput telemetry channel and background consumer service
        services.Configure<TelemetryChannelOptions>(configuration.GetSection(TelemetryChannelOptions.SectionName));
        services.AddSingleton<ITelemetryChannel, TelemetryChannel>();
        services.AddHostedService<TelemetryChannelConsumerService>();

        // In-memory idempotency store
        services.AddMemoryCache();
        services.AddSingleton<IIdempotencyStore, MemoryIdempotencyStore>();

        return services;
    }
}
