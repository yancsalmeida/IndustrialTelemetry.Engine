using IndustrialTelemetry.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialTelemetry.Infrastructure.Persistence;

public class IndustrialDbContext : DbContext
{
    public IndustrialDbContext(DbContextOptions<IndustrialDbContext> options)
        : base(options)
    {
    }

    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<TelemetryMeasurement> Telemetry => Set<TelemetryMeasurement>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<ProcessAlert> ProcessAlerts => Set<ProcessAlert>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Batch Entity Configuration
        modelBuilder.Entity<Batch>(b =>
        {
            b.ToTable("batches");
            b.HasKey(x => x.Id);

            b.Property(x => x.Code)
                .HasMaxLength(64)
                .IsRequired();
            b.HasIndex(x => x.Code).IsUnique();

            b.Property(x => x.ProductRecipe)
                .HasMaxLength(128)
                .IsRequired();

            b.Property(x => x.ReactorId)
                .HasMaxLength(32)
                .IsRequired();
            b.HasIndex(x => x.ReactorId);

            b.Property(x => x.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            b.Property(x => x.AbortReason)
                .HasMaxLength(512);

            b.Property(x => x.OperatorNotes)
                .HasMaxLength(1024);

            b.HasMany(x => x.Telemetry)
                .WithOne()
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);

            b.HasMany(x => x.Alerts)
                .WithOne()
                .HasForeignKey(x => x.BatchId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 2. Telemetry Measurement Entity Configuration
        modelBuilder.Entity<TelemetryMeasurement>(t =>
        {
            t.ToTable("telemetry_measurements");
            t.HasKey(x => x.Id);

            t.Property(x => x.SensorTag)
                .HasMaxLength(32)
                .IsRequired();

            t.Property(x => x.Unit)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            t.Property(x => x.Quality)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            // Composite indexes for high-frequency time-series queries
            t.HasIndex(x => new { x.BatchId, x.TimestampUtc });
            t.HasIndex(x => new { x.BatchId, x.SensorTag });
        });

        // 3. Alert Rule Entity Configuration
        modelBuilder.Entity<AlertRule>(r =>
        {
            r.ToTable("alert_rules");
            r.HasKey(x => x.Id);

            r.Property(x => x.SensorTag)
                .HasMaxLength(32)
                .IsRequired();
            r.HasIndex(x => x.SensorTag);

            r.Property(x => x.Severity)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            r.Property(x => x.Description)
                .HasMaxLength(256);
        });

        // 4. Process Alert Entity Configuration
        modelBuilder.Entity<ProcessAlert>(a =>
        {
            a.ToTable("process_alerts");
            a.HasKey(x => x.Id);

            a.Property(x => x.SensorTag)
                .HasMaxLength(32)
                .IsRequired();

            a.Property(x => x.Severity)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            a.Property(x => x.Message)
                .HasMaxLength(512)
                .IsRequired();

            a.Property(x => x.AcknowledgedBy)
                .HasMaxLength(64);

            a.HasIndex(x => new { x.BatchId, x.TimestampUtc });
            a.HasIndex(x => x.Acknowledged);
        });
    }
}
