using IndustrialTelemetry.Domain.Entities;
using IndustrialTelemetry.Domain.Events;
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
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

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

        // 5. Outbox Message Entity Configuration
        modelBuilder.Entity<OutboxMessage>(o =>
        {
            o.ToTable("outbox_messages");
            o.HasKey(x => x.Id);

            o.Property(x => x.EventType)
                .HasMaxLength(128)
                .IsRequired();

            o.Property(x => x.AggregateType)
                .HasMaxLength(64)
                .IsRequired();

            o.Property(x => x.AggregateId)
                .HasMaxLength(64)
                .IsRequired();

            o.Property(x => x.Payload)
                .HasColumnType("text")
                .IsRequired();

            o.Property(x => x.CorrelationId)
                .HasMaxLength(128);

            o.Property(x => x.LastError)
                .HasMaxLength(2048);

            o.HasIndex(x => new { x.ProcessedOnUtc, x.OccurredOnUtc });
            o.HasIndex(x => new { x.AggregateType, x.AggregateId });
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ConvertDomainEventsToOutboxMessages();
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        ConvertDomainEventsToOutboxMessages();
        return base.SaveChanges();
    }

    private void ConvertDomainEventsToOutboxMessages()
    {
        var entitiesWithEvents = ChangeTracker.Entries<IHasDomainEvents>()
            .Where(e => e.Entity.DomainEvents.Count > 0)
            .Select(e => e.Entity)
            .ToList();

        if (entitiesWithEvents.Count == 0)
            return;

        foreach (var entity in entitiesWithEvents)
        {
            string aggregateType = entity.GetType().Name;
            string aggregateId = entity switch
            {
                Batch b => b.Id.ToString(),
                ProcessAlert a => a.Id.ToString(),
                _ => Guid.NewGuid().ToString()
            };

            foreach (var domainEvent in entity.DomainEvents)
            {
                var outboxMessage = OutboxMessage.FromDomainEvent(domainEvent, aggregateType, aggregateId);
                OutboxMessages.Add(outboxMessage);
            }

            entity.ClearDomainEvents();
        }
    }
}
