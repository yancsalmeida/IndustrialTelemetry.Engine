using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndustrialTelemetry.Infrastructure.Persistence;

public static class DbInitializer
{
    public static async Task InitializeAsync(IndustrialDbContext context, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            await context.Database.EnsureCreatedAsync(ct);

            if (!await context.AlertRules.AnyAsync(ct))
            {
                logger.LogInformation("Seeding default industrial safety alert rules...");

                var rules = new[]
                {
                    AlertRule.Create("TT-101", 60.0, 115.0, AlertSeverity.Warning, "Reactor temperature safe operational envelope"),
                    AlertRule.Create("TT-101", null, 135.0, AlertSeverity.Critical, "CRITICAL: Reactor thermal runaway excursion limit"),
                    AlertRule.Create("PT-101", null, 4.0, AlertSeverity.Warning, "Reactor vapor space pressure high warning"),
                    AlertRule.Create("PT-101", null, 5.5, AlertSeverity.Critical, "CRITICAL: Reactor vessel overpressure relief limit"),
                    AlertRule.Create("LT-101", null, 8.0, AlertSeverity.Warning, "Surge tank high liquid level warning"),
                    AlertRule.Create("LT-101", null, 9.5, AlertSeverity.Critical, "CRITICAL: Surge tank high level emergency trip"),
                    AlertRule.Create("pH-101", 6.0, 8.5, AlertSeverity.Warning, "Polymerization batch pH stability envelope"),
                    AlertRule.Create("RPM-101", 50.0, 450.0, AlertSeverity.Warning, "Agitator mechanical speed nominal operating band"),
                    AlertRule.CreateRateOfChange("TT-101", maxRateOfChangePerMinute: 5.0, minRateOfChangePerMinute: null, AlertSeverity.Critical, "CRITICAL: Reactor thermal runaway rapid rate-of-temperature-rise excursion (dT/dt > 5.0 °C/min)", windowSeconds: 30),
                    AlertRule.CreateRateOfChange("PT-101", maxRateOfChangePerMinute: null, minRateOfChangePerMinute: -1.0, AlertSeverity.Warning, "Rapid reactor depressurization leak detection (dP/dt < -1.0 bar/min)", windowSeconds: 20)
                };

                await context.AlertRules.AddRangeAsync(rules, ct);
                await context.SaveChangesAsync(ct);
            }

            if (!await context.Batches.AnyAsync(ct))
            {
                logger.LogInformation("Seeding sample initial industrial batch...");

                var seedBatch = Batch.Create(
                    code: "BATCH-2026-POLYA",
                    productRecipe: "Ethylene-Vinyl-Acetate-Copolymer-Grade-A",
                    reactorId: "R-101",
                    targetVolumeM3: 20.0,
                    operatorNotes: "Commissioning batch for continuous SCADA telemetry ingestion validation.");

                // Start batch so it is ready to ingest telemetry immediately
                seedBatch.Start(DateTime.UtcNow.AddMinutes(-30));

                await context.Batches.AddAsync(seedBatch, ct);
                await context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing or seeding the database.");
            throw;
        }
    }
}
